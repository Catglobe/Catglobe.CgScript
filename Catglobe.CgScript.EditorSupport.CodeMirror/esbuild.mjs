import { build } from "esbuild";

await build({
   entryPoints: ["src/index.ts"],
   bundle: true,
   format: "esm",
   outfile: "../Catglobe.CgScript.EditorSupport.CodeMirror.AspNet/wwwroot/cgscript-cm6.js",
   minify: false,
   sourcemap: false,
   target: ["es2020"],
   // Do NOT mark any packages as external — we bundle everything so the output
   // is a single self-contained browser ESM file with no CDN dependencies.
});

console.log("esbuild: wwwroot/cgscript-cm6.js written");
