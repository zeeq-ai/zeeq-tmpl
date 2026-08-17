import { defineConfig } from "kubb";
import { pluginTs } from "@kubb/plugin-ts";
import { pluginFetch } from "@kubb/plugin-fetch";

export default defineConfig({
  input: "./src/api/zeeq-tmpl-api.json",
  output: {
    path: "./src/api/generated",
    clean: true,
  },
  plugins: [
    pluginTs({
      output: {
        path: "./types",
        mode: "directory"
      },
      enum: {
        type: "asConst"
      },
      optionalType: "questionTokenAndUndefined",
    }),
    pluginFetch({
      baseURL: "http://localhost:5138",
      output: {
        path: "./clients",
        mode: "directory",
      },
    }),
  ],
});
