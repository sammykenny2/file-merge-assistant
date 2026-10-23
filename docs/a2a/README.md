# A2A 1.0 Example Set

> Targets specification release 1.0.0 and wire version 1.0.
> Reference checked 2026-09-05. Examples are not a running service.

## Baseline and Bindings

- Public discovery: `/.well-known/agent-card.json`.
- Authenticated extended card: JSON-RPC `GetExtendedAgentCard`; the HTTP+JSON
  binding uses `GET /extendedAgentCard` relative to that binding's base URL.
- Send `A2A-Version: 1.0` on client requests. The specification also permits
  the version query parameter. Omission means legacy 0.3 semantics.
- Agent Card `version` describes the application; each
  `supportedInterfaces[*].protocolVersion` describes the wire protocol.

## Files

| Files | Purpose |
|-------|---------|
| `agent-card.public.(json|yaml).example` | Public discovery metadata |
| `agent-card.authenticated.(json|yaml).example` | Extended metadata after authentication |
| `jsonrpc-*.request.json.example` | JSON-RPC request bodies |
| `jsonrpc-*.request.http.example` | Complete HTTP request examples with version metadata |
| `jsonrpc-send-message.response.json.example` | Completed task response |
| `jsonrpc-send-streaming-message.response.sse.example` | Task lifecycle stream |

JSON and YAML cards describe the same data. JSON is the discovery wire format.
Bracketed values are placeholders. Example timestamps are syntactically valid
fixture values, not actual event times.

## Adaptation

Replace identities, URLs, and workflows with the implementation's values.
Advertise only bindings and capabilities that are implemented. The example
authentication requirements allow OpenID Connect; the extended card also shows
mutual TLS as an alternative. Public visibility of a discovery card does not
make task execution anonymous. `securitySchemes` defines mechanisms, while
`securityRequirements` selects the required combinations.

A2A `skills` describe externally advertised capabilities, not SKILL.md packages.
Keep those capabilities aligned with the service; do not publish local-only
instructions or credentials.

The task stream begins with a Task, continues with status/artifact updates for
the same task and context, and ends with a terminal status update. A direct
Message response is a separate single-event stream.

If implemented, document `ListTasks`, `SubscribeToTask`, and the push notification
configuration methods and their error responses. The starter does not provide
complete examples for every optional method. Push notifications are disabled
in these sample cards until an implementation supplies them.

## Upgrade and Verification

Full examples are distributed through the optional `a2a` flavor. Fresh base/preset
projects contain only an integration pointer. Existing children with current or
legacy A2A examples remain on the upgrade path automatically; an implicit retained
A2A layer precedes explicit custom flavors.

Legacy `agent-card.(json|yaml).example` and `task.(json|yaml).example` files are
retired. Update-Project preserves existing files and stages review copies;
merge those deliberately. Differing pending review copies are preserved as
conflicts, not overwritten. Run a real client/server integration test before
claiming runtime interoperability. JSON parsing alone does not check protocol
behavior, authorization, or SDK support.

In the bootstrap checkout, `config/standards-lock.json` pins the normative Protobuf,
license, and a locally authored JSON Schema example profile. The offline suite also
checks HTTP metadata, JSON/YAML agreement, server message direction/context, stream
identity and SSE framing. See the bootstrap's `docs/testing.md` and
`docs/standards-maintenance.md`; those developer checks are not a child runtime
dependency or a complete A2A conformance suite.

This repository retains `agent-card.json.example` and `agent-card.yaml.example`
as compatibility copies of the public card. The legacy `task.json.example` and
`task.yaml.example` contain deprecation notices linking to the replacement files.
The examples describe a hypothetical agent; File Merge Assistant does not expose
an A2A server.

## Fixed References

- [A2A 1.0.0 specification](https://a2a-protocol.org/v1.0.0/specification/)
- [A2A 1.0.0 definition](https://a2a-protocol.org/v1.0.0/definitions/)
- [Streaming semantics](https://a2a-protocol.org/v1.0.0/specification/#312-send-streaming-message)
- [Version metadata](https://a2a-protocol.org/v1.0.0/specification/#361-client-responsibilities)
