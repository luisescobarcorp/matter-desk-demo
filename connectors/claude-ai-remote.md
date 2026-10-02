# Claude.ai — custom connector (remote MCP, no header)

1. In Claude.ai open **Settings → Connectors** (Pro/Max/Team/Enterprise plans) and choose **Add custom connector**.
2. Name: `MatterDesk (JDU)`.
3. Remote MCP server URL: `https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/JDU`
4. Leave OAuth client id / secret empty — the demo needs no authentication; the operator is the last path segment.
5. Save, then enable the connector in a chat (the tools icon) and ask, for example:
   *"Search MatterDesk for Falcon and list the documents on the matter it finds."*

Use `/mcp/LES` instead of `/mcp/JDU` to connect as the operator who is **not** granted the restricted matter;
the same question then returns zero hits. `/mcp/PAR` is the third seeded operator. An unknown code is 403.

Claude Desktop (the app) can use the same URL as a remote connector, or the local stdio bridge in
[`claude-desktop.json`](claude-desktop.json).

Source and the other connector files: https://github.com/luisescobarcorp/matter-desk-demo/tree/main/connectors
