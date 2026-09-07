using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiffPatch;
using DiffPatch.Data;

namespace KiloVisualStudioExtension.Utils
{
  public static class UnifiedPatchService
  {
    public static string Apply(
        string originalContent,
        string patch)
    {
      if (originalContent == null)
        throw new ArgumentNullException(
            nameof(originalContent));

      if (patch == null)
        throw new ArgumentNullException(
            nameof(patch));

     
      var files = DiffParserHelper.Parse(patch, Environment.NewLine).ToArray();
      if (files.Length > 0)
      {
        FileDiff fileDiff = files[0];
        return PatchHelper.Patch(originalContent, fileDiff.Chunks, Environment.NewLine);
      }
      throw new NotImplementedException(
          "Add the DiffPatch API call here.");
    }

    public static async Task<string> ApplyToFileAsync(
        string originalFilePath,
        string patch)
    {
      if (!File.Exists(originalFilePath))
      {
        throw new FileNotFoundException(
            "The original file was not found.",
            originalFilePath);
      }

      var originalContent =
          await AsyncUtils.ReadAllTextAsync(originalFilePath);

      return Apply(
          originalContent,
          patch);
    }
  }
}
