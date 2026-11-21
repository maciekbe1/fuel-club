// Pass-through proxy in front of the e2e API. A request carrying the cookie `e2e-fault=<503|drop>`
// fails /api/auth/me on purpose, so a single test can simulate an API outage without affecting others.
import http from "node:http";

const port = Number(process.env.PROXY_PORT);
const target = new URL(process.env.PROXY_TARGET);

http
  .createServer((req, res) => {
    const fault = /(?:^|;\s*)e2e-fault=([^;]+)/.exec(req.headers.cookie ?? "")?.[1];
    if (fault && req.url?.startsWith("/api/auth/me")) {
      if (fault === "drop") return req.socket.destroy();
      res.writeHead(Number(fault), { "content-type": "application/problem+json" });
      return res.end(JSON.stringify({ status: Number(fault), title: "Injected fault" }));
    }
    const upstream = http.request(
      { host: target.hostname, port: target.port, path: req.url, method: req.method, headers: req.headers },
      (response) => {
        res.writeHead(response.statusCode ?? 502, response.headers);
        response.pipe(res);
      },
    );
    upstream.on("error", () => res.destroy());
    req.pipe(upstream);
  })
  .listen(port);
