import { createInertiaApp } from "@inertiajs/react";
import { createRoot, hydrateRoot } from "react-dom/client";
import { resolve } from "./resolve";

createInertiaApp({
  id: "kitchen-sink",
  resolve,
  title: (title) => `${title} · InertiaNet`,
  progress: { color: "#427b61" },
  setup({ el, App, props }) {
    if (el.hasAttribute("data-server-rendered"))
      hydrateRoot(el, <App {...props} />);
    else createRoot(el).render(<App {...props} />);
  },
});
