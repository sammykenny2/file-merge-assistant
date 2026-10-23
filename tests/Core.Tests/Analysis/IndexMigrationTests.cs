using FileMergeAssistant.Index;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class IndexMigrationTests
{
  [Fact]
  public void Schema_4_Upgrade_Preserves_Unpublished_Checkpoints_Without_Inventing_Observations()
  {
    var path = Path.Combine(TestEnvironment.DataRoot, $"schema4-{Guid.NewGuid():N}.db");
    using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
    {
      old.Open();
      IndexDatabase.Execute(old, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/index/schema-v3.sql")));
      IndexDatabase.Execute(old, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/index/migration-v4.sql")));
      IndexDatabase.Execute(old,
        """
        INSERT INTO sources(id,name,root,is_protected,skip_regenerable,skip_cloud,regenerable_names,exclusions,added_at,position)
          VALUES ('s','s','C:\fixture',0,1,1,'','','2026-10-23',1);
        INSERT INTO files(id,source_id,rel_path,size,mtime,hash,seen) VALUES (1,'s','a',4,100,NULL,1);
        INSERT INTO jobs(id,source_id,title,kind,state,phase,queued_at,position,analysis_snapshot,analysis_generation)
          VALUES ('job','s','staged','Update','Interrupted','Confirm','2026-10-23',1,'{}',1);
        INSERT INTO analysis_results(job_id,generation,file_id,hash,size,mtime) VALUES ('job',1,1,'sha256-v1:fixture',4,100);
        """);
    }
    using var connection = IndexDatabase.OpenOrCreate(path).Open();
    Assert.Equal((long)IndexDatabase.SchemaVersion, IndexDatabase.Scalar<long>(connection, "PRAGMA user_version"));
    Assert.Equal("sha256-v1:fixture", IndexDatabase.Scalar<string>(connection, "SELECT hash FROM analysis_results"));
    Assert.Null(IndexDatabase.Scalar<string>(connection, "SELECT observation FROM analysis_results"));
    Assert.Null(IndexDatabase.Scalar<string>(connection, "SELECT path FROM analysis_results"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT analysis_complete FROM jobs"));
    using var backup = new SqliteConnection($"Data Source={path}.v4.bak;Mode=ReadOnly;Pooling=False");
    backup.Open();
    Assert.Equal(4L, IndexDatabase.Scalar<long>(backup, "PRAGMA user_version"));
    Assert.Equal(1L, IndexDatabase.Scalar<long>(backup, "SELECT analysis_generation FROM jobs"));
  }

  [Fact]
  public void Schema_3_Upgrade_Backs_Up_And_Preserves_History_While_Marking_Legacy_Evidence()
  {
    var path = Path.Combine(TestEnvironment.DataRoot, $"schema3-{Guid.NewGuid():N}.db");
    var legacyHash = "xxh128:" + new string('a', 32);
    using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
    {
      old.Open();
      IndexDatabase.Execute(old, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/index/schema-v3.sql")));
      IndexDatabase.Execute(old,
        """
        INSERT INTO sources(id,name,root,is_protected,skip_regenerable,skip_cloud,regenerable_names,exclusions,added_at,position)
          VALUES ('s','s','C:\fixture',0,1,1,'','','2026-10-23',1);
        INSERT INTO files(id,source_id,rel_path,size,mtime,hash,seen) VALUES (1,'s','a',4,100,$hash,1),(2,'s','bad',4,100,'blake3:unknown',1);
        INSERT INTO jobs(id,source_id,title,kind,state,phase,queued_at,position)
          VALUES ('done','s','old completed','FirstScan','Completed','Done','2026-10-23',1),
                 ('paused','s','old paused','Update','Paused','Confirm','2026-10-23',2);
        INSERT INTO batches(id,ran_at) VALUES ('batch','2026-10-23');
        INSERT INTO batch_items(batch_id,id,position,stack_id,kind,target,detail,state,bytes,file_count,failed_files,retried,
          original_path,recycled_path,source_stamp,keeper_path)
          VALUES ('batch','item',1,'stack','Recycle','target','detail','Done',4,1,0,0,'original','recycled','legacy-stamp','keeper');
        INSERT INTO decisions(stack_id,json,status,decided_at) VALUES ('stack','{"legacy":true}','Pending','2026-10-23');
        """, ("$hash", legacyHash));
    }
    var database = IndexDatabase.OpenOrCreate(path);
    using var connection = database.Open();
    Assert.Equal((long)IndexDatabase.SchemaVersion, IndexDatabase.Scalar<long>(connection, "PRAGMA user_version"));
    Assert.Equal(2L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(legacyHash, IndexDatabase.Scalar<string>(connection, "SELECT hash FROM files WHERE id=1"));
    Assert.Equal("legacy-unattributed", IndexDatabase.Scalar<string>(connection, "SELECT engine_id FROM file_fingerprints WHERE file_id=1"));
    Assert.Equal("xxh128-v1", IndexDatabase.Scalar<string>(connection, "SELECT algorithm FROM file_fingerprints WHERE file_id=1"));
    Assert.Equal(1L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM file_fingerprints"));
    Assert.Equal("NeedsNewJob", IndexDatabase.Scalar<string>(connection, "SELECT state FROM jobs WHERE id='paused'"));
    Assert.Equal("Completed", IndexDatabase.Scalar<string>(connection, "SELECT state FROM jobs WHERE id='done'"));
    Assert.Equal("legacy-stamp", IndexDatabase.Scalar<string>(connection, "SELECT source_stamp FROM batch_items"));
    Assert.Equal("recycled", IndexDatabase.Scalar<string>(connection, "SELECT recycled_path FROM batch_items"));
    Assert.Equal("{\"legacy\":true}", IndexDatabase.Scalar<string>(connection, "SELECT json FROM decisions"));
    using var backup = new SqliteConnection($"Data Source={path}.v3.bak;Mode=ReadOnly;Pooling=False");
    backup.Open();
    Assert.Equal(3L, IndexDatabase.Scalar<long>(backup, "PRAGMA user_version"));
    Assert.Equal("Paused", IndexDatabase.Scalar<string>(backup, "SELECT state FROM jobs WHERE id='paused'"));
    Assert.Equal("keeper", IndexDatabase.Scalar<string>(backup, "SELECT keeper_path FROM batch_items"));
  }
}
