import type { PageProps } from "@inertiajs/core";

export interface Shared extends PageProps {
  auth: { name?: string };
  request: { id: string; at: string };
  reference: { framework: string; loadedAt: string; teams: string[] };
  xsrfToken: string;
  memberMessage?: string;
  renderHook: string;
}
export type Entry = { id: number; title: string; team: string };
