using System.Runtime.CompilerServices;

// The plot tests need two hooks the app never does: a memo of their own (so the
// statistics they pin cannot be polluted by another test class rendering a
// plot in parallel) and `NiceStep` with a deliberately wrong `log10`, which is
// the family's mechanism guard for the decade pin. Both are internal.
[assembly: InternalsVisibleTo("Md.Core.Tests")]
