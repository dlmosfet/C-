using System.IO;
using System.Threading.Tasks;

namespace MarriageStatistics.Services;

public class JsonProcessorAdapter : IJsonProcessor
{
    public async Task ProcessAsync(string filePath, TextWriter output)
    {
        // Delegate to existing static JsonProcessor2 for backward compatibility
        await JsonProcessor2.ProcessJsonAsync(filePath, output);
    }
}