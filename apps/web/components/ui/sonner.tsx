"use client";

import { Toaster as Sonner, type ToasterProps } from "sonner";

/** App-wide toasts (sonner), styled with the theme tokens. */
export function Toaster(props: ToasterProps) {
  return (
    <Sonner
      theme="system"
      position="top-center"
      className="toaster group"
      containerAriaLabel="Powiadomienia"
      toastOptions={{ closeButtonAriaLabel: "Zamknij powiadomienie" }}
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
        } as React.CSSProperties
      }
      {...props}
    />
  );
}
