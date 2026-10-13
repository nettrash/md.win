# Formatting reference

The TypeScript reference `docs/formatting.md` was written against, and the
generator of the cross-port oracle `formatting-vectors.json`.

- `formatting.ts` — `format(text, selectionStart, selectionEnd, op)` and
  `wrapTyped(text, selectionStart, selectionEnd, typed)`, UTF-16 offsets.
- `generate.ts` — the hand-written cases (`‸` a caret, `⟨…⟩` a selection),
  run through the reference; writes the JSON and a readable `summary.txt`.

Regenerate after changing a rule or a case, from this folder:

```sh
npx tsx generate.ts ../../tests/Md.Core.Tests/Fixtures/formatting-vectors.json
```

(`tsx` is any TypeScript runner; bundling with esbuild and running the result
with Node works the same.) Then copy the JSON **verbatim** into the other
ports' test fixtures in the same change — md, md.macOS and md.Android each
carry an identical copy. A disputed vector is changed here first;
no port may carry a vector of its own.
