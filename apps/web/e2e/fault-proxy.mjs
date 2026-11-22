// Pass-through proxy in front of the e2e API. A request carrying the cookie `e2e-fault=<503|drop>`
// fails /api/auth/me on purpose, so a single test can simulate an API outage without affecting others.
// For the chat stream (POST /api/chat): `sse-split` re-sends the stream in 3-byte pieces and holds it
// before the first `delta` frame; `sse-cut` closes the connection there (no done, no error).
import http from "node:http";

const port = Number(process.env.PROXY_PORT);
const target = new URL(process.env.PROXY_TARGET);

http
  .createServer((req, res) => {
    const fault = /(?:^|;\s*)e2e-fault=([^;]+)/.exec(
      req.headers.cookie ?? "",
    )?.[1];
    const outage = fault === "drop" || /^\d{3}$/.test(fault ?? "");
    if (outage && req.url?.startsWith("/api/auth/me")) {
      if (fault === "drop") return req.socket.destroy();
      res.writeHead(Number(fault), {
        "content-type": "application/problem+json",
      });
      return res.end(
        JSON.stringify({ status: Number(fault), title: "Injected fault" }),
      );
    }
    const upstream = http.request(
      {
        host: target.hostname,
        port: target.port,
        path: req.url,
        method: req.method,
        headers: req.headers,
      },
      (response) => {
        res.writeHead(response.statusCode ?? 502, response.headers);
        const chat = req.method === "POST" && req.url === "/api/chat";
        if (chat && (fault === "sse-split" || fault === "sse-cut"))
          return reshape(response, res, fault);
        response.pipe(res);
      },
    );
    upstream.on("error", () => res.destroy());
    req.pipe(upstream);
  })
  .listen(port);

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Byte slices, so multi-byte characters (Polish letters) are split across pieces too.
async function writeSplit(res, text) {
  const bytes = Buffer.from(text, "utf8");
  for (let i = 0; i < bytes.length; i += 3) {
    res.write(bytes.subarray(i, i + 3));
    await sleep(2);
  }
}

/** Buffers the upstream stream, then replays it split, held or cut before the first delta frame. */
function reshape(upstream, res, fault) {
  let body = "";
  upstream.setEncoding("utf8");
  upstream.on("data", (chunk) => (body += chunk));
  upstream.on("end", async () => {
    const at = body.indexOf("event: delta");
    const head = at < 0 ? body : body.slice(0, at);
    await writeSplit(res, head);
    if (fault === "sse-cut") return res.destroy();
    await sleep(1500);
    await writeSplit(res, at < 0 ? "" : body.slice(at));
    res.end();
  });
}
