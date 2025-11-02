// TestCaseEditorApp/Import/JamaAllDataEnricher.cs
using System.IO;
using System.Text.RegularExpressions;
using TestCaseEditorApp.MVVM.Models;
using static TestCaseEditorApp.Import.JamaRequirementMapper;

namespace TestCaseEditorApp.Import
{
    public sealed class JamaAllDataEnricher : IRequirementEnricher
    {
        private readonly string _path;
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _index;

        public JamaAllDataEnricher(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            var text = File.ReadAllText(_path);
            _index = BuildJamaIndexById(text);
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
            System.Diagnostics.Debug.WriteLine($"[Jama Enricher] Matched {hit}/{requirements.Count} by ID from '{_path}'.");
        }

        // ===== helpers =====
        private static readonly Regex HeaderRx = new(
            @"^(?<id>[A-Z0-9_-]+-REQ_[A-Z]+-\d+)\s+(?<heading>\d[\d\.]*)\s+(?<name>.+)$",
            RegexOptions.Compiled);

        private static Dictionary<string, IReadOnlyDictionary<string, string>> BuildJamaIndexById(string raw)
        {
            var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string? curHeader = null;
            var curLines = new List<string>();

            void Flush()
            {
                if (curHeader == null) return;
                var m = HeaderRx.Match(curHeader);
                if (!m.Success) { curHeader = null; curLines.Clear(); return; }

                var id = m.Groups["id"].Value.Trim();
                var kvLines = curLines.Where(l => l.IndexOf('\t') > 0);
                var kv = JamaKvBuilder.Build(kvLines);
                map[id] = kv;

                curHeader = null;
                curLines.Clear();
            }

            foreach (var line in lines)
            {
                if (HeaderRx.IsMatch(line))
                {
                    Flush();
                    curHeader = line;
                }
                else if (curHeader != null)
                {
                    curLines.Add(line);
                }
            }
            Flush();
            return map;
        }
    }
}

