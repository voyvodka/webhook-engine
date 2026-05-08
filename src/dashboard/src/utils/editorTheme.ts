import { EditorView } from "@codemirror/view";
import { tags as t } from "@lezer/highlight";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { json } from "@codemirror/lang-json";

export const editorTheme = EditorView.theme(
  {
    "&": {
      backgroundColor: "transparent",
      color: "#fafafa",
      fontSize: "12px",
      fontFamily:
        "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace"
    },
    ".cm-content": { padding: "8px 0", caretColor: "#22d3ee" },
    ".cm-gutters": {
      backgroundColor: "transparent",
      color: "#71717a",
      borderRight: "1px solid #1e1e22"
    },
    ".cm-activeLine": { backgroundColor: "transparent" },
    ".cm-activeLineGutter": { backgroundColor: "transparent" },
    ".cm-cursor": { borderLeftColor: "#22d3ee" },
    "&.cm-focused": { outline: "none" },
    "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection": {
      backgroundColor: "rgba(34, 211, 238, 0.18)"
    }
  },
  { dark: true }
);

export const editorHighlight = HighlightStyle.define([
  { tag: t.string, color: "#86efac" },
  { tag: t.number, color: "#fbbf24" },
  { tag: t.bool, color: "#f472b6" },
  { tag: t.null, color: "#71717a" },
  { tag: t.propertyName, color: "#22d3ee" },
  { tag: t.keyword, color: "#c4b5fd" },
  { tag: t.punctuation, color: "#a1a1aa" }
]);

/** JSON payload editor: theme + highlight + json language + line wrapping */
export const jsonEditorExtensions = [
  editorTheme,
  syntaxHighlighting(editorHighlight),
  json(),
  EditorView.lineWrapping
];

/** Plain expression editor (JMESPath etc.): theme + line wrapping only */
export const expressionEditorExtensions = [editorTheme, EditorView.lineWrapping];
