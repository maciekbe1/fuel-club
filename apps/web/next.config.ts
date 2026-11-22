import type { NextConfig } from "next";

const apiInternalUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5271";

const nextConfig: NextConfig = {
  // E2E runs its own dev server next to the regular one; a separate build dir avoids the dev lock.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
  // The Docker image runs the self-contained server (apps/web/Dockerfile).
  output: process.env.NEXT_OUTPUT === "standalone" ? "standalone" : undefined,
  // Next would gzip the proxied chat stream (SSE) and send it only when it ends. The API also sends
  // Cache-Control: no-transform, but a session cookie renewal replaces that header. Compression,
  // if wanted, belongs to the reverse proxy in front of Next.
  compress: false,
  experimental: {
    agentFeedback: true,
  },
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${apiInternalUrl}/api/:path*` },
    ];
  },
};

export default nextConfig;
