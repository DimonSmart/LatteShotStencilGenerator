using LatteShotStencilGenerator;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddSingleton<ISvgImportAdapter, SvgImportAdapter>();
builder.Services.AddSingleton<ICaptionFontOutlineAdapter, BundledCaptionFontOutlineAdapter>();
await builder.Build().RunAsync();
