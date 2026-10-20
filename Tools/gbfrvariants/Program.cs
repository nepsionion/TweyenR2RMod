// Exports one glTF per material variant (vars/N.mmat). All variants share one .bin (geometry is identical)
// and one folder of textures, so shared textures are only decoded once.
using GBFRDataTools.Models;

string input = args[0], model = args[1], output = args[2];
var vars = Directory.GetFiles(Path.Combine(input, "model", model[..2], model, "vars"), "*.mmat")
    .Select(p => int.Parse(Path.GetFileNameWithoutExtension(p))).Order()
    .Where(v => args.Length < 4 || v == int.Parse(args[3])); // optional: only this variant (run variants in separate processes so granite page files get released)
foreach (int v in vars)
{
    var converter = new MInfoToGLTFConverter(input);
    converter.LoadModel(model, v);
    converter.Convert(output, new MInfoToGLTFConverterOptions { ThrowOnMissingTextures = true });
    string dst = Path.Combine(output, $"{model}_var{v:D2}.gltf");
    File.Move(Path.Combine(output, $"{model}.gltf"), dst, overwrite: true);
    Console.WriteLine($"var {v} -> {dst}");
}
