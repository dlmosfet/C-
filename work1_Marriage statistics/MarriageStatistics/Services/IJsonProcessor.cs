using System.IO;
using System.Threading.Tasks;

namespace MarriageStatistics.Services;

public interface IJsonProcessor
{
    Task ProcessAsync(string filePath, TextWriter output);
}