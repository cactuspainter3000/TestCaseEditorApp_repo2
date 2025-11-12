using System.Collections.ObjectModel;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Preset container exposing a collection of Chip items for UI consumption.
    /// Created from your DefaultPreset instances via PresetConverter.
    /// </summary>
    public class Preset
    {
        public Preset()
        {
            Items = new ObservableCollection<Chip>();
        }

        public Preset(string name) : this()
        {
            Name = name;
        }

        public string? Name { get; set; }

        public ObservableCollection<Chip> Items { get; }

        public string? Description { get; set; }
    }
}