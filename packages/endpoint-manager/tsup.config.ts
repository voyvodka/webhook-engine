import { defineConfig } from "tsup";

export default defineConfig({
  entry: ["src/index.ts"],
  format: ["esm"],
  dts: true,
  // clean: false — the `clean` npm script handles dist removal so the build
  // order is always: clean → build:css → tsup. This prevents tsup from wiping
  // style.css when build:css runs before tsup in some invocations.
  clean: false,
  sourcemap: true,
  treeshake: true,
  external: ["react", "react-dom"]
});
