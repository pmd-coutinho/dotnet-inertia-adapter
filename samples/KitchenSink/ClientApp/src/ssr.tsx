import createServer from "@inertiajs/core/server";
import { createInertiaApp } from "@inertiajs/react";
import { renderToString } from "react-dom/server";
import { resolve } from "./resolve";

const render = await createInertiaApp({
  id: "kitchen-sink",
  resolve,
  title: (title) => `${title} · InertiaNet`,
});
if (typeof render !== "function")
  throw new Error("SSR render function was not initialized");
createServer(
  (page) => {
    if (page.props.forceSsrFailure)
      throw new Error("Requested kitchen sink SSR fallback");
    return render(page, renderToString);
  },
  { host: "127.0.0.1", port: Number(process.env.SSR_PORT ?? 13715) },
);
