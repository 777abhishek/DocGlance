using System.Reflection;
using System.Runtime.CompilerServices;

// Manual attribute: GenerateAssemblyInfo=false in QuickView.csproj suppresses the
// SDK-generated InternalsVisibleTo item, so the test project needs it declared here.
[assembly: InternalsVisibleTo("QuickView.Tests")]

// GenerateAssemblyInfo=false also means the <AssemblyVersion>/<FileVersion>/<InformationalVersion>
// properties in QuickView.csproj are NOT emitted — without these attributes the app reports
// version 0.0.0.0 (and the updater would treat every release as newer).
// Keep these in sync with the values in QuickView.csproj when bumping a release.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("14.10.26.0")]
[assembly: AssemblyInformationalVersion("14.10.26")]
[assembly: AssemblyCompany("777abhishek")]
[assembly: AssemblyProduct("QuickView")]
