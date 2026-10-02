# ChatGPT — remote MCP connector (no header)

1. In ChatGPT open **Settings → Connectors** and enable **Developer mode** under *Advanced*
   (Plus/Pro/Team/Enterprise plans; an admin may need to allow custom connectors on a workspace).
2. Choose **Create** (custom connector).
3. Name: `MatterDesk (JDU)`.
4. MCP server URL: `https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/JDU`
5. Authentication: **No authentication**. Confirm you trust the connector and create it.
6. In a new chat, add the connector from the tools menu (Developer mode) and ask, for example:
   *"Search MatterDesk for Falcon and list the documents on the matter it finds."*

Use `/mcp/LES` to connect as the operator who is **not** granted the restricted matter; the same question
then returns zero hits. `/mcp/PAR` is the third seeded operator. An unknown code is 403.

Source and the other connector files: https://github.com/luisescobarcorp/matter-desk-demo/tree/main/connectors
