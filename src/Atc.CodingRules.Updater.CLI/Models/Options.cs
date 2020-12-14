namespace Atc.CodingRules.Updater.CLI.Models
{
    public class Options
    {
        public OptionsMappings Mappings { get; set; } = new OptionsMappings();

        public bool HasMappingsPaths() => Mappings.HasMappingsPaths();

        public override string ToString()
        {
            return $"{nameof(Mappings)}: ({Mappings})";
        }
    }
}