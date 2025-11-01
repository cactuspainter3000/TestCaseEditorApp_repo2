// TestCaseEditorApp/Import/JamaAllDataDocxEnricher.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Import
{
    public sealed class JamaAllDataDocxEnricher : IRequirementEnricher
    {
        private readonly string _path;
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _index;

        public JamaAllDataDocxEnricher(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _index = BuildIndexFromDocx(_path);
        }

        public void Enrich(IList<Requirement> requirements)
        {
            if (requirements == null || requirements.Count == 0) return;
            int hit = 0;
            foreach (var r in requirements)
            {
                if (string.IsNullOrWhiteSpace(r.Item)) continue;
                if (_index.TryGetValue(r.Item, out var kv))
                {
                    JamaRequirementMapper.MapFromKv(r, kv);
                    hit++;
                }
            }
            System.Diagnostics.Debug.WriteLine($"[Jama DOCX Enricher] Matched {hit}/{requirements.Count} by ID from '{_path}'.");
        }

        // ===== helpers =====
        private static readonly Regex HeaderRx = new(
            @"^(?<id>[A-Z0-9_-]+-REQ_[A-Z]+-\d+)\s+(?<heading>\d[\d\.]*)\s+(?<name>.+)$",
            RegexOptions.Compiled);

        private static Dictionary<string, IReadOnlyDictionary<string, string>> BuildIndexFromDocx(string docxPath)
        {
            var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using var doc = WordprocessingDocument.Open(docxPath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return map;

            var elements = body.Elements().ToList();

            string? curId = null;
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Flush()
            {
                if (!string.IsNullOrWhiteSpace(curId))
                    map[curId!] = new Dictionary<string, string>(kv);
                kv.Clear();
                curId = null;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                var el = elements[i];

                if (el is Paragraph p)
                {
                    var text = (p.InnerText ?? string.Empty).Trim();
                    var m = HeaderRx.Match(text);
                    if (m.Success)
                    {
                        Flush();
                        curId = m.Groups["id"].Value.Trim();
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(curId)) continue;

                    // Paragraph "Key<TAB>Value"
                    int tab = text.IndexOf('\t');
                    if (tab > 0)
                    {
                        var key = text[..tab].Trim().TrimEnd(':');
                        var val = text[(tab + 1)..].Trim();
                        if (key.Length > 0)
                            kv[key] = Append(kv.TryGetValue(key, out var prev) ? prev : string.Empty, val);
                    }
                }
                else if (el is Table tbl && !string.IsNullOrWhiteSpace(curId))
                {
                    // Two-column KV tables
                    foreach (var row in tbl.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>().ToList();
                        if (cells.Count < 2) continue;

                        var key = GetText(cells[0]).Trim().TrimEnd(':');
                        var val = string.Join(" ", cells.Skip(1).Select(c => GetText(c).Trim())).Trim();
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        kv[key] = Append(kv.TryGetValue(key, out var prev) ? prev : string.Empty, val);
                    }
                }
            }

            Flush();
            return map;
        }

        private static string GetText(OpenXmlElement e)
            => (e.InnerText ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        private static string Append(string a, string b)
            => string.IsNullOrWhiteSpace(a) ? b : (string.IsNullOrWhiteSpace(b) ? a : a + " " + b);
    }
}

