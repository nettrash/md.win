// generate.ts — writes formatting-vectors.json from the cases below.
// Case notation: ‸ is a caret; ⟨ … ⟩ is a selection. Neither character appears in a case's text.
import * as fs from 'node:fs';
import { apply, format, wrapTyped, type Op } from './formatting';

type Case = [id: string, op: Op, marked: string];

function parse(marked: string): { text: string; s: number; e: number } {
  const caret = marked.indexOf('‸');
  if (caret >= 0) return { text: marked.replace('‸', ''), s: caret, e: caret };
  const a = marked.indexOf('⟨');
  const b = marked.indexOf('⟩');
  if (a < 0 || b < a) throw new Error(`no selection in ${marked}`);
  return { text: marked.slice(0, a) + marked.slice(a + 1, b) + marked.slice(b + 1), s: a, e: b - 1 };
}

function show(text: string, s: number, e: number): string {
  return s === e ? text.slice(0, s) + '‸' + text.slice(s) : text.slice(0, s) + '⟨' + text.slice(s, e) + '⟩' + text.slice(e);
}

const cases: Case[] = [
  // bold
  ['bold-caret-empty-doc', 'bold', '‸'],
  ['bold-caret-mid-word', 'bold', 'ab‸cd'],
  ['bold-caret-in-empty-pair-removes', 'bold', 'x **‸** y'],
  ['bold-caret-in-italic-pair-inserts', 'bold', 'x *‸* y'],
  ['bold-wrap-word', 'bold', 'say ⟨hello⟩ now'],
  ['bold-wrap-trims-blanks', 'bold', 'say⟨ hello ⟩now'],
  ['bold-wrap-trims-newline', 'bold', 'a⟨word\n⟩b'],
  ['bold-all-blank-selection-inserts-pair', 'bold', 'a⟨   ⟩b'],
  ['bold-unwrap-outside', 'bold', 'say **⟨hello⟩** now'],
  ['bold-unwrap-inside', 'bold', 'say ⟨**hello**⟩ now'],
  ['bold-on-italic-wraps', 'bold', 'say *⟨hello⟩* now'],
  ['bold-on-bold-italic-outside', 'bold', 'say ***⟨hello⟩*** now'],
  ['bold-on-bold-italic-inside', 'bold', 'say ⟨***hello***⟩ now'],
  ['bold-selection-all-stars-wraps', 'bold', 'a ⟨****⟩ b'],
  ['bold-multiline', 'bold', '⟨one\ntwo⟩'],
  ['bold-unicode', 'bold', 'é ⟨Привет 👋⟩ ok'],
  ['bold-reversed-not-possible-caret-end', 'bold', 'word‸'],
  // italic
  ['italic-caret', 'italic', 'a‸b'],
  ['italic-caret-in-empty-pair-removes', 'italic', '*‸*'],
  ['italic-caret-in-bold-pair-inserts', 'italic', '**‸**'],
  ['italic-wrap', 'italic', '⟨word⟩'],
  ['italic-unwrap-outside', 'italic', '*⟨word⟩*'],
  ['italic-unwrap-inside', 'italic', '⟨*word*⟩'],
  ['italic-on-bold-wraps-outside', 'italic', '**⟨word⟩**'],
  ['italic-on-bold-inside-wraps', 'italic', '⟨**word**⟩'],
  ['italic-on-bold-italic-outside', 'italic', '***⟨word⟩***'],
  ['italic-on-bold-italic-inside', 'italic', '⟨***word***⟩'],
  ['italic-underscore-outside', 'italic', '_⟨word⟩_'],
  ['italic-underscore-inside', 'italic', '⟨_word_⟩'],
  ['italic-double-underscore-wraps', 'italic', '__⟨word⟩__'],
  ['italic-lopsided-wraps', 'italic', '*⟨word⟩**'],
  // code
  ['code-caret', 'code', 'x‸'],
  ['code-caret-in-empty-pair-removes', 'code', 'a `‸` b'],
  ['code-wrap', 'code', 'run ⟨npm test⟩ now'],
  ['code-unwrap-outside', 'code', 'run `⟨npm test⟩` now'],
  ['code-unwrap-inside', 'code', 'run ⟨`npm test`⟩ now'],
  ['code-double-backticks-wraps', 'code', '``⟨a⟩``'],
  ['code-selection-one-backtick', 'code', '⟨`⟩'],
  // strikethrough
  ['strike-caret', 'strikethrough', '‸'],
  ['strike-caret-in-pair-removes', 'strikethrough', '~~‸~~'],
  ['strike-wrap', 'strikethrough', 'it was ⟨wrong⟩'],
  ['strike-unwrap-outside', 'strikethrough', 'it was ~~⟨wrong⟩~~'],
  ['strike-unwrap-inside', 'strikethrough', 'it was ⟨~~wrong~~⟩'],
  ['strike-single-tilde-wraps', 'strikethrough', '~⟨a⟩~'],
  // link
  ['link-caret', 'link', 'see ‸ here'],
  ['link-wrap-text', 'link', 'see ⟨the docs⟩ here'],
  ['link-wrap-trims', 'link', 'see⟨ the docs ⟩here'],
  ['link-url', 'link', 'see ⟨https://example.com/a?b=c⟩'],
  ['link-url-case', 'link', '⟨HTTP://EXAMPLE.COM⟩'],
  ['link-url-with-space-is-text', 'link', '⟨https://a b⟩'],
  ['link-ftp-is-text', 'link', '⟨ftp://x⟩'],
  ['link-all-blank', 'link', 'a⟨  ⟩b'],
  // headings
  ['h1-plain-caret', 'heading1', 'Ti‸tle'],
  ['h1-caret-empty-line', 'heading1', '‸'],
  ['h1-caret-at-col0', 'heading1', '‸Title'],
  ['h1-toggle-off', 'heading1', '# Ti‸tle'],
  ['h2-replaces-h1', 'heading2', '# Ti‸tle'],
  ['h3-replaces-h6', 'heading3', '###### Ti‸tle'],
  ['h1-caret-inside-marker', 'heading1', '#‸## Title'],
  ['h2-bare-hashes', 'heading2', '##‸'],
  ['h1-seven-hashes-is-text', 'heading1', '####### x‸'],
  ['h1-hash-no-space-is-text', 'heading1', '#tag‸'],
  ['h2-indented-3', 'heading2', '   te‸xt'],
  ['h2-indented-4-goes-col0', 'heading2', '    te‸xt'],
  ['h2-indented-tab-goes-col0', 'heading2', '\tte‸xt'],
  ['h1-multiline-mixed', 'heading1', '⟨one\n# two\nthree⟩'],
  ['h1-multiline-all-off', 'heading1', '⟨# one\n\n# two⟩'],
  ['h1-multiline-skips-blank', 'heading1', '⟨one\n\ntwo⟩'],
  ['h1-selection-ends-col0-next', 'heading1', '⟨one\n⟩two'],
  ['h6', 'heading6', 'x‸'],
  ['h4-in-second-line', 'heading4', 'a\nb‸\nc'],
  ['cycle-none-to-1', 'headingCycle', 'x‸'],
  ['cycle-1-to-2', 'headingCycle', '# x‸'],
  ['cycle-2-to-3', 'headingCycle', '## x‸'],
  ['cycle-3-to-none', 'headingCycle', '### x‸'],
  ['cycle-5-to-none', 'headingCycle', '##### x‸'],
  ['cycle-multiline-follows-first', 'headingCycle', '⟨# a\nb\n### c⟩'],
  // bullet list
  ['bullet-caret', 'bulletList', 'it‸em'],
  ['bullet-caret-empty-line', 'bulletList', '‸'],
  ['bullet-toggle-off', 'bulletList', '- it‸em'],
  ['bullet-star-off', 'bulletList', '* it‸em'],
  ['bullet-indented-keeps-indent', 'bulletList', '  it‸em'],
  ['bullet-from-ordered', 'bulletList', '12. it‸em'],
  ['bullet-from-ordered-paren', 'bulletList', '3) it‸em'],
  ['bullet-from-task', 'bulletList', '- [x] it‸em'],
  ['bullet-multiline-mixed', 'bulletList', '⟨a\n- b\nc⟩'],
  ['bullet-multiline-all-off', 'bulletList', '⟨- a\n\n+ b⟩'],
  ['bullet-multiline-with-task-adds', 'bulletList', '⟨- a\n- [ ] b⟩'],
  ['bullet-dash-no-space-is-text', 'bulletList', '-x‸'],
  ['bullet-bare-dash-off', 'bulletList', '-‸'],
  ['bullet-ten-digits-is-text', 'bulletList', '1234567890. x‸'],
  ['bullet-caret-before-marker', 'bulletList', '‸- item'],
  // task list
  ['task-caret', 'taskList', 'it‸em'],
  ['task-from-bullet', 'taskList', '- it‸em'],
  ['task-from-star-bullet', 'taskList', '* it‸em'],
  ['task-from-bare-dash', 'taskList', '-‸'],
  ['task-from-ordered', 'taskList', '1. it‸em'],
  ['task-toggle-off', 'taskList', '- [ ] it‸em'],
  ['task-checked-off', 'taskList', '- [X] it‸em'],
  ['task-box-at-end', 'taskList', '- [ ]‸'],
  ['task-box-no-space-is-bullet', 'taskList', '- [ ]x‸'],
  ['task-indented', 'taskList', '    - it‸em'],
  ['task-multiline-mixed', 'taskList', '⟨- [ ] a\nb⟩'],
  ['task-multiline-all-off', 'taskList', '⟨- [ ] a\n  - [x] b⟩'],
  // quote
  ['quote-caret', 'quote', 'te‸xt'],
  ['quote-caret-empty', 'quote', '‸'],
  ['quote-off', 'quote', '> te‸xt'],
  ['quote-off-no-space', 'quote', '>te‸xt'],
  ['quote-off-nested-one-level', 'quote', '> > te‸xt'],
  ['quote-indented-off', 'quote', '  > te‸xt'],
  ['quote-multiline-adds', 'quote', '⟨a\n\nb⟩'],
  ['quote-multiline-mixed', 'quote', '⟨> a\nb⟩'],
  ['quote-multiline-all-off', 'quote', '⟨> a\n>\n> b⟩'],
  ['quote-multiline-blank-ws-gets-marker', 'quote', '⟨a\n  \nb⟩'],
  ['quote-list-line', 'quote', '- it‸em'],
  // indent / outdent
  ['indent-caret', 'indent', 'te‸xt'],
  ['indent-caret-col0', 'indent', '‸text'],
  ['indent-list', 'indent', '- it‸em'],
  ['indent-multiline-skips-blank', 'indent', '⟨a\n\nb⟩'],
  ['indent-selection-ends-col0-next', 'indent', '⟨a\n⟩b'],
  ['outdent-two', 'outdent', '    te‸xt'],
  ['outdent-one', 'outdent', ' te‸xt'],
  ['outdent-tab', 'outdent', '\t\tte‸xt'],
  ['outdent-nothing', 'outdent', 'te‸xt'],
  ['outdent-caret-in-indent', 'outdent', ' ‸ text'],
  ['outdent-multiline', 'outdent', '⟨  a\nb\n   c⟩'],
  ['outdent-crlf-is-content', 'outdent', '  a\r‸'],
];

const typedCases: [string, string, string][] = [
  ['typed-star', '*', 'a ⟨word⟩ b'],
  ['typed-underscore', '_', '⟨word⟩'],
  ['typed-backtick', '`', '⟨x = 1⟩'],
  ['typed-dollar', '$', 'area ⟨\\pi r^2⟩'],
  ['typed-keeps-blanks', '*', '⟨ a ⟩'],
  ['typed-multiline', '`', '⟨a\nb⟩'],
  ['typed-caret-is-typing', '*', 'a‸b'],
  ['typed-other-char', '~', '⟨word⟩'],
  ['typed-letter', 'a', '⟨word⟩'],
  ['typed-unicode', '*', '⟨😀⟩'],
];

function jsonString(s: string): string {
  let out = '"';
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c === 0x22) out += '\\"';
    else if (c === 0x5c) out += '\\\\';
    else if (c >= 0x20 && c <= 0x7e) out += s[i];
    else out += '\\u' + c.toString(16).toUpperCase().padStart(4, '0');
  }
  return out + '"';
}

const ids = new Set<string>();
const lines: string[] = [];
const summary: string[] = [];
function emit(id: string, op: string, text: string, s: number, e: number, edit: ReturnType<typeof format>, typed?: string) {
  if (ids.has(id)) throw new Error(`duplicate ${id}`);
  ids.add(id);
  const expected = edit === null ? 'null' :
    `{"start": ${edit.start}, "end": ${edit.end}, "text": ${jsonString(edit.text)}, "selectionStart": ${edit.selectionStart}, "selectionEnd": ${edit.selectionEnd}}`;
  const head = typed === undefined ? `"op": ${jsonString(op)}` : `"typed": ${jsonString(typed)}`;
  lines.push(`    {"id": ${jsonString(id)}, ${head}, "text": ${jsonString(text)}, "selectionStart": ${s}, "selectionEnd": ${e}, "expected": ${expected}}`);
  const after = edit === null ? '(no edit)' : show(apply(text, edit), edit.selectionStart, edit.selectionEnd);
  summary.push(`${id.padEnd(40)} ${JSON.stringify(show(text, s, e)).padEnd(34)} → ${JSON.stringify(after)}`);
}

const formatLines: string[] = [];
for (const [id, op, marked] of cases) {
  const { text, s, e } = parse(marked);
  emit(id, op, text, s, e, format(text, s, e, op));
}
formatLines.push(...lines.splice(0));
const typedLines: string[] = [];
for (const [id, typed, marked] of typedCases) {
  const { text, s, e } = parse(marked);
  emit(id, 'typed', text, s, e, wrapTyped(text, s, e, typed), typed);
}
typedLines.push(...lines.splice(0));

const json = `{
  "version": 1,
  "comment": "md formatting commands, 1.6. Offsets are UTF-16 code units; a line break is U+000A only. expected is the edit to the ORIGINAL text (replace [start, end) with text) and the selection in the RESULTING text, or null for no edit. Every unit outside U+0020-U+007E is a \\\\uXXXX escape. Generated by the TypeScript reference docs/formatting.md was written against; a disputed vector is changed here first, in one commit across md, md.macOS, md.Android and md.win.",
  "format": [
${formatLines.join(',\n')}
  ],
  "wrapTyped": [
${typedLines.join(',\n')}
  ]
}
`;
fs.writeFileSync(process.argv[2] ?? 'formatting-vectors.json', json);
fs.writeFileSync('summary.txt', summary.join('\n') + '\n');
JSON.parse(json);
console.log(`${cases.length} format + ${typedCases.length} wrapTyped vectors`);
