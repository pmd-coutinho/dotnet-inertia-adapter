import type { ComponentType, ReactNode } from "react";
import Layout from "./Layout";
import Home from "./pages/Home";
import Contacts from "./pages/Contacts";
import Forms from "./pages/Forms";
import Props from "./pages/Props";
import Merging from "./pages/Merging";
import Feed from "./pages/Feed";
import Polling from "./pages/Polling";
import Navigation from "./pages/Navigation";
import { Rendering, ErrorPage, Minimal } from "./pages/Rendering";
import type { ComponentName, PropsFor } from "./pathfinder/types/page-map";

type PageRegistry = {
  [Name in ComponentName]: ComponentType<PropsFor<Name>> & {
    layout?: (page: ReactNode) => ReactNode;
  };
};

const pages: PageRegistry = {
  Home,
  Contacts,
  Forms,
  Props,
  Merging,
  Feed,
  Polling,
  Navigation,
  Rendering,
  Error: ErrorPage,
  Minimal,
};
Object.values(pages).forEach((page) => {
  page.layout = (child) => <Layout>{child}</Layout>;
});
export function resolve(name: string) {
  if (!Object.hasOwn(pages, name))
    throw new Error(`Unknown kitchen sink page: ${name}`);
  return pages[name as ComponentName];
}
