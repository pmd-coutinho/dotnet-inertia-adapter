import { rmSync } from "node:fs";
export default function teardown() {
  if (process.env.KITCHEN_SINK_TEST_DIRECTORY)
    rmSync(process.env.KITCHEN_SINK_TEST_DIRECTORY, {
      recursive: true,
      force: true,
    });
}
