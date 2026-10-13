using System.Runtime.CompilerServices;

// Manual attribute: GenerateAssemblyInfo=false in QuickView.csproj suppresses the
// SDK-generated InternalsVisibleTo item, so the test project needs it declared here.
[assembly: InternalsVisibleTo("QuickView.Tests")]
