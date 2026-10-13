// formatting.ts — the TypeScript reference for md's formatting commands (1.6, item 10).
// Generates formatting-vectors.json; docs/formatting.md (md.win) is written against it.
// Offsets are UTF-16 code units. A line break is U+000A only.

export type Op =
  | 'bold' | 'italic' | 'code' | 'strikethrough' | 'link'
  | 'heading1' | 'heading2' | 'heading3' | 'heading4' | 'heading5' | 'heading6' | 'headingCycle'
  | 'bulletList' | 'taskList' | 'quote' | 'indent' | 'outdent';

export interface Edit {
  start: number; // replaced range in the ORIGINAL text
  end: number;
  text: string; // replacement
  selectionStart: number; // in the RESULTING text
  selectionEnd: number;
}

const SP = 0x20, TAB = 0x09, LF = 0x0a;
const isBlankUnit = (c: number) => c === SP || c === TAB || c === LF;

// MARK: inline

interface Inline { ch: string; width: number; present: (before: number, after: number) => boolean; remove: number; }

const INLINE: Record<'bold' | 'italic' | 'code' | 'strikethrough', Inline> = {
  bold: { ch: '*', width: 2, present: (a, b) => a >= 2 && b >= 2, remove: 2 },
  italic: { ch: '*', width: 1, present: (a, b) => a % 2 === 1 && b % 2 === 1, remove: 1 },
  code: { ch: '`', width: 1, present: (a, b) => a === 1 && b === 1, remove: 1 },
  strikethrough: { ch: '~', width: 2, present: (a, b) => a >= 2 && b >= 2, remove: 2 },
};

/** Run of `ch` ending at p (units p-1, p-2, … that equal ch). */
function runBefore(text: string, p: number, ch: string, floor = 0): number {
  let n = 0;
  while (p - n - 1 >= floor && text[p - n - 1] === ch) n++;
  return n;
}
/** Run of `ch` starting at p. */
function runAfter(text: string, p: number, ch: string, ceiling = text.length): number {
  let n = 0;
  while (p + n < ceiling && text[p + n] === ch) n++;
  return n;
}

/** [s', e'): the selection without leading and trailing SP, TAB, LF. */
function trimmed(text: string, s: number, e: number): [number, number] {
  let a = s, b = e;
  while (a < b && isBlankUnit(text.charCodeAt(a))) a++;
  while (b > a && isBlankUnit(text.charCodeAt(b - 1))) b--;
  return [a, b];
}

function inline(text: string, s: number, e: number, kind: keyof typeof INLINE): Edit {
  const spec = INLINE[kind];
  const m = spec.ch.repeat(spec.width);
  const alt = kind === 'italic' ? '_' : null;

  if (s === e) {
    // Between an empty pair of exactly this marker: take the pair out.
    if (runBefore(text, s, spec.ch) === spec.width && runAfter(text, s, spec.ch) === spec.width) {
      return { start: s - spec.width, end: s + spec.width, text: '', selectionStart: s - spec.width, selectionEnd: s - spec.width };
    }
    return { start: s, end: s, text: m + m, selectionStart: s + spec.width, selectionEnd: s + spec.width };
  }

  // 1. The selection carries the markers itself.
  for (const ch of alt ? [spec.ch, alt] : [spec.ch]) {
    const lead = runAfter(text, s, ch, e);
    const trail = runBefore(text, e, ch, s);
    if (lead + trail < e - s) {
      const present = ch === spec.ch ? spec.present(lead, trail) : lead === 1 && trail === 1;
      const remove = ch === spec.ch ? spec.remove : 1;
      if (present) {
        const inner = text.slice(s + remove, e - remove);
        return { start: s, end: e, text: inner, selectionStart: s, selectionEnd: s + inner.length };
      }
    }
  }
  // 2. The markers sit just outside the selection.
  for (const ch of alt ? [spec.ch, alt] : [spec.ch]) {
    const before = runBefore(text, s, ch);
    const after = runAfter(text, e, ch);
    const present = ch === spec.ch ? spec.present(before, after) : before === 1 && after === 1;
    const remove = ch === spec.ch ? spec.remove : 1;
    if (present) {
      const inner = text.slice(s, e);
      return { start: s - remove, end: e + remove, text: inner, selectionStart: s - remove, selectionEnd: s - remove + inner.length };
    }
  }
  // 3. Wrap what is selected, without its surrounding blanks.
  const [a, b] = trimmed(text, s, e);
  if (a === b) {
    return { start: s, end: s, text: m + m, selectionStart: s + spec.width, selectionEnd: s + spec.width };
  }
  return { start: a, end: b, text: m + text.slice(a, b) + m, selectionStart: a + spec.width, selectionEnd: b + spec.width };
}

function startsWithAsciiIgnoringCase(text: string, prefix: string): boolean {
  if (text.length < prefix.length) return false;
  for (let i = 0; i < prefix.length; i++) {
    let c = text.charCodeAt(i);
    if (c >= 0x41 && c <= 0x5a) c += 32;
    if (c !== prefix.charCodeAt(i)) return false;
  }
  return true;
}

function link(text: string, s: number, e: number): Edit {
  const [a, b] = trimmed(text, s, e);
  if (a === b) {
    return { start: s, end: s, text: '[]()', selectionStart: s + 1, selectionEnd: s + 1 };
  }
  const inner = text.slice(a, b);
  let blank = false;
  for (let i = 0; i < inner.length; i++) if (isBlankUnit(inner.charCodeAt(i))) blank = true;
  if (!blank && (startsWithAsciiIgnoringCase(inner, 'http://') || startsWithAsciiIgnoringCase(inner, 'https://'))) {
    return { start: a, end: b, text: `[](${inner})`, selectionStart: a + 1, selectionEnd: a + 1 };
  }
  const replacement = `[${inner}](url)`;
  const urlAt = a + 1 + inner.length + 2;
  return { start: a, end: b, text: replacement, selectionStart: urlAt, selectionEnd: urlAt + 3 };
}

// MARK: lines

interface Line { start: number; end: number; } // [start, end) without the LF

function lineAt(text: string, p: number): Line {
  let start = p;
  while (start > 0 && text.charCodeAt(start - 1) !== LF) start--;
  let end = p;
  while (end < text.length && text.charCodeAt(end) !== LF) end++;
  return { start, end };
}

/** The lines the selection touches; a non-empty selection ending at a line's column 0 leaves that line out. */
function touchedLines(text: string, s: number, e: number): Line[] {
  const first = lineAt(text, s);
  let lastPos = e;
  if (e > s && e > 0 && text.charCodeAt(e - 1) === LF) lastPos = e - 1;
  const lines: Line[] = [first];
  let cur = first;
  while (cur.end < lastPos) {
    cur = lineAt(text, cur.end + 1);
    lines.push(cur);
  }
  return lines;
}

interface LineChange { at: number; old: number; text: string; } // within the line: replace [at, at+old) with text
type LineRule = (line: string) => LineChange | null;

function isBlankLine(line: string): boolean {
  for (let i = 0; i < line.length; i++) {
    const c = line.charCodeAt(i);
    if (c !== SP && c !== TAB) return false;
  }
  return true;
}

function indentEnd(line: string): number {
  let i = 0;
  while (i < line.length && (line.charCodeAt(i) === SP || line.charCodeAt(i) === TAB)) i++;
  return i;
}

function isDigit(c: number) { return c >= 0x30 && c <= 0x39; }

/** Heading level (1–6) and the length of its marker incl. one following space, or null. Up to 3 leading spaces. */
function heading(line: string): { at: number; level: number; length: number } | null {
  let i = 0;
  while (i < 3 && line.charCodeAt(i) === SP) i++;
  let n = 0;
  while (line.charCodeAt(i + n) === 0x23 /* # */) n++;
  if (n < 1 || n > 6) return null;
  const after = i + n;
  if (after === line.length) return { at: i, level: n, length: n };
  if (line.charCodeAt(after) === SP) return { at: i, level: n, length: n + 1 };
  return null;
}

interface ListMarker { at: number; kind: 'bullet' | 'ordered'; length: number; task: boolean; contentAt: number; }

/** The list marker after the indentation: `-`/`*`/`+` or 1–9 digits with `.`/`)`, then SP or end of line; a task adds `[ ]`/`[x]`/`[X]` then SP or end. */
function listMarker(line: string): ListMarker | null {
  const at = indentEnd(line);
  let i = at;
  let kind: 'bullet' | 'ordered';
  const c = line.charCodeAt(i);
  if (c === 0x2d || c === 0x2a || c === 0x2b) {
    kind = 'bullet';
    i++;
  } else {
    let d = 0;
    while (d < 9 && isDigit(line.charCodeAt(i + d))) d++;
    if (d === 0) return null;
    const p = line.charCodeAt(i + d);
    if (p !== 0x2e && p !== 0x29) return null;
    kind = 'ordered';
    i += d + 1;
  }
  if (i === line.length) return { at, kind, length: i - at, task: false, contentAt: i };
  if (line.charCodeAt(i) !== SP) return null;
  const markerLength = i + 1 - at;
  const box = line.slice(i + 1, i + 4);
  if (box === '[ ]' || box === '[x]' || box === '[X]') {
    const after = i + 4;
    if (after === line.length) return { at, kind, length: markerLength, task: true, contentAt: after };
    if (line.charCodeAt(after) === SP) return { at, kind, length: markerLength, task: true, contentAt: after + 1 };
  }
  return { at, kind, length: markerLength, task: false, contentAt: i + 1 };
}

/** `>` after up to 3 spaces, with one following space if there is one. */
function quoteMarker(line: string): { at: number; length: number } | null {
  let i = 0;
  while (i < 3 && line.charCodeAt(i) === SP) i++;
  if (line.charCodeAt(i) !== 0x3e) return null;
  return { at: i, length: line.charCodeAt(i + 1) === SP ? 2 : 1 };
}

function applyLines(text: string, s: number, e: number, plan: (lines: string[], participating: boolean[]) => (LineChange | null)[]): Edit | null {
  const lines = touchedLines(text, s, e);
  const strings = lines.map((l) => text.slice(l.start, l.end));
  const multi = lines.length > 1;
  const participating = strings.map((l) => !(multi && isBlankLine(l)));
  const changes = plan(strings, participating);
  if (changes.every((c) => c === null || (c.old === 0 && c.text === ''))) return null;

  // A position on a changed line: before the replaced span it stays; at or after the span's end
  // it moves with the span; inside the span it goes to the end of the new text. A position past
  // a line (on a later one, or the excluded line after the selection) takes that line's delta.
  const map = (p: number): number => {
    let delta = 0;
    for (let k = 0; k < lines.length; k++) {
      const change = changes[k];
      const line = lines[k];
      const lineDelta = change ? change.text.length - change.old : 0;
      if (p > line.end) {
        delta += lineDelta;
        continue;
      }
      if (!change) return p + delta;
      const a = line.start + change.at;
      if (p >= a + change.old) return p + delta + lineDelta;
      if (p <= a) return p + delta;
      return a + delta + change.text.length;
    }
    return p + delta;
  };

  const out: string[] = strings.map((l, k) => {
    const c = changes[k];
    return c ? l.slice(0, c.at) + c.text + l.slice(c.at + c.old) : l;
  });
  return {
    start: lines[0].start,
    end: lines[lines.length - 1].end,
    text: out.join('\n'),
    selectionStart: map(s),
    selectionEnd: map(e),
  };
}

function setHeading(line: string, level: number): LineChange | null {
  const h = heading(line);
  if (h) {
    if (level === 0) return { at: h.at, old: h.length, text: '' };
    const marker = '#'.repeat(level) + ' ';
    if (h.level === level && h.length === level + 1) return null;
    return { at: h.at, old: h.length, text: marker };
  }
  if (level === 0) return null;
  const at = indentEnd(line) <= 3 && !line.slice(0, indentEnd(line)).includes('\t') ? indentEnd(line) : 0;
  return { at, old: 0, text: '#'.repeat(level) + ' ' };
}

function headingOp(text: string, s: number, e: number, level: number | 'cycle'): Edit | null {
  return applyLines(text, s, e, (lines, on) => {
    const first = lines.findIndex((_, k) => on[k]);
    let target: number;
    if (level === 'cycle') {
      const current = first < 0 ? 0 : heading(lines[first])?.level ?? 0;
      target = current === 0 ? 1 : current === 1 ? 2 : current === 2 ? 3 : 0;
    } else {
      const all = lines.every((l, k) => !on[k] || heading(l)?.level === level);
      target = all ? 0 : level;
    }
    return lines.map((l, k) => (on[k] ? setHeading(l, target) : null));
  });
}

function bulletOp(text: string, s: number, e: number): Edit | null {
  return applyLines(text, s, e, (lines, on) => {
    const all = lines.every((l, k) => {
      if (!on[k]) return true;
      const m = listMarker(l);
      return m !== null && m.kind === 'bullet' && !m.task;
    });
    return lines.map((l, k) => {
      if (!on[k]) return null;
      const m = listMarker(l);
      if (all) return { at: m!.at, old: m!.contentAt - m!.at, text: '' };
      if (m === null) return { at: indentEnd(l), old: 0, text: '- ' };
      if (m.task || m.kind === 'ordered') return { at: m.at, old: m.contentAt - m.at, text: '- ' };
      return null;
    });
  });
}

function taskOp(text: string, s: number, e: number): Edit | null {
  return applyLines(text, s, e, (lines, on) => {
    const all = lines.every((l, k) => !on[k] || listMarker(l)?.task === true);
    return lines.map((l, k) => {
      if (!on[k]) return null;
      const m = listMarker(l);
      if (all) return { at: m!.at, old: m!.contentAt - m!.at, text: '' };
      if (m === null) return { at: indentEnd(l), old: 0, text: '- [ ] ' };
      if (m.task) return null;
      if (m.kind === 'ordered') return { at: m.at, old: m.contentAt - m.at, text: '- [ ] ' };
      // A bullet: the box goes after its marker and space (a bare `-` gets its space too).
      return { at: m.at, old: m.contentAt - m.at, text: l.slice(m.at, m.at + 1) + ' [ ] ' };
    });
  });
}

function quoteOp(text: string, s: number, e: number): Edit | null {
  const lines = touchedLines(text, s, e);
  const strings = lines.map((l) => text.slice(l.start, l.end));
  const multi = lines.length > 1;
  // Blank lines do not decide whether the selection is "all quoted", but when quoting they get a bare `>`.
  const all = strings.every((l) => (multi && isBlankLine(l)) || quoteMarker(l) !== null);
  return applyLines(text, s, e, (ls) =>
    ls.map((l) => {
      if (all) {
        const q = quoteMarker(l);
        return q ? { at: q.at, old: q.length, text: '' } : null;
      }
      if (quoteMarker(l) !== null) return null;
      if (multi && isBlankLine(l)) return { at: 0, old: 0, text: '>' };
      return { at: 0, old: 0, text: '> ' };
    }),
  );
}

function indentOp(text: string, s: number, e: number): Edit | null {
  return applyLines(text, s, e, (lines, on) => lines.map((_, k) => (on[k] ? { at: 0, old: 0, text: '  ' } : null)));
}

function outdentOp(text: string, s: number, e: number): Edit | null {
  return applyLines(text, s, e, (lines) =>
    lines.map((l) => {
      if (l.charCodeAt(0) === TAB) return { at: 0, old: 1, text: '' };
      const n = l.charCodeAt(0) === SP ? (l.charCodeAt(1) === SP ? 2 : 1) : 0;
      return n === 0 ? null : { at: 0, old: n, text: '' };
    }),
  );
}

export function format(text: string, s: number, e: number, op: Op): Edit | null {
  if (s > e) [s, e] = [e, s];
  switch (op) {
    case 'bold':
    case 'italic':
    case 'code':
    case 'strikethrough':
      return inline(text, s, e, op);
    case 'link':
      return link(text, s, e);
    case 'heading1': return headingOp(text, s, e, 1);
    case 'heading2': return headingOp(text, s, e, 2);
    case 'heading3': return headingOp(text, s, e, 3);
    case 'heading4': return headingOp(text, s, e, 4);
    case 'heading5': return headingOp(text, s, e, 5);
    case 'heading6': return headingOp(text, s, e, 6);
    case 'headingCycle': return headingOp(text, s, e, 'cycle');
    case 'bulletList': return bulletOp(text, s, e);
    case 'taskList': return taskOp(text, s, e);
    case 'quote': return quoteOp(text, s, e);
    case 'indent': return indentOp(text, s, e);
    case 'outdent': return outdentOp(text, s, e);
  }
}

/** Typing one of * _ ` $ over a non-empty selection wraps it; anything else is ordinary typing (null). */
export function wrapTyped(text: string, s: number, e: number, typed: string): Edit | null {
  if (s > e) [s, e] = [e, s];
  if (s === e) return null;
  if (typed !== '*' && typed !== '_' && typed !== '`' && typed !== '$') return null;
  return { start: s, end: e, text: typed + text.slice(s, e) + typed, selectionStart: s + 1, selectionEnd: e + 1 };
}

export function apply(text: string, edit: Edit): string {
  return text.slice(0, edit.start) + edit.text + text.slice(edit.end);
}
