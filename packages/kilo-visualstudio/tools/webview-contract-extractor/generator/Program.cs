using WebViewContractGenerator;

var options = new GeneratorOptions
{
    ContractPath = args.Contains("--contract") 
        ? args[args.IndexOf("--contract") + 1]
        : "contract/WebViewContract.json",
    OutputPath = args.Contains("--output")
        ? args[args.IndexOf("--output") + 1]
        : "../KiloVisualStudioExtension/WebView/Generated",
    Namespace = args.Contains("--namespace")
        ? args[args.IndexOf("--namespace") + 1]
        : "KiloVisualStudioExtension.WebView.Generated"
};

var generator = new Generator(options);
generator.Generate();
