import { createServer } from "node:http";
import { createImdsMockHandler, IMDS_MOCK_PORT, readImdsMockCredentials } from "./imds-mock.js";

function main(): void {
  const credentials = readImdsMockCredentials(process.env);
  if (credentials === undefined) {
    console.error(
      "imds-mock: no AWS credentials in the environment; credential paths will answer 503 " +
        "(start the stack through ./scripts/dev.sh to feed short-lived credentials)",
    );
  } else if (credentials.expiration !== undefined) {
    console.log(`imds-mock: serving credentials expiring at ${credentials.expiration}`);
  }
  const handler = createImdsMockHandler({ credentials });
  const server = createServer((request, response) => {
    let result: { status: number; body: string };
    try {
      result = handler({
        method: request.method ?? "",
        path: (request.url ?? "").split("?")[0] ?? "",
        headers: request.headers,
      });
    } catch (error) {
      console.error("imds-mock request failed", error);
      result = { status: 500, body: "internal error" };
    }
    response.writeHead(result.status, { "content-type": "text/plain" });
    response.end(result.body);
  });
  server.listen(IMDS_MOCK_PORT, () => {
    console.log(`imds-mock listening on ${IMDS_MOCK_PORT}`);
  });
  const shutdown = (): void => {
    server.close(() => process.exit(0));
  };
  process.on("SIGTERM", shutdown);
  process.on("SIGINT", shutdown);
}

main();
