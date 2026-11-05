# Third-party notices

IniaAi.Platform is licensed under the Apache License 2.0, in `LICENSE`. Some files in this repository, and every package that a build restores, come from other people and organizations. Each stays under its own licence, which this file names. The licence of this repository does not change them.

## Files in this repository that Inia did not write

### Apache ECharts, ZRender and parts of d3, in the chart bundle

- Files: `IniaAi.Services/Resources/chartBuilder/charts.global.js`, a copy of the Vue app's `chart-builder/` build. It bundles the Vue app's chart code with ECharts.
- Versions: the first lines of `charts.global.js` name the ECharts and ZRender versions it embeds.
- Upstream: <https://github.com/apache/echarts>, <https://github.com/ecomfe/zrender>, <https://github.com/d3/d3>
- Licences: Apache-2.0 (ECharts), BSD-3-Clause (ZRender, d3). The texts and the ECharts notice are in the same folder: `LICENSE-echarts`, `NOTICE-echarts`, `LICENSE-zrender` and `LICENSE-d3`.

### Bootswatch Pulse 5.3.3, on Bootstrap 5.3.3

- Files: `IniaAi.WebApp/wwwroot/bootstrap.min.css`
- Upstream: <https://github.com/thomaspark/bootswatch>, <https://github.com/twbs/bootstrap>
- Licence: MIT, for both.

```text
The MIT License (MIT)

Copyright (c) 2013 Thomas Park

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

```text
The MIT License (MIT)

Copyright (c) 2011-2024 The Bootstrap Authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### elkjs 0.12.0

- Files: `IniaAi.WebApp/wwwroot/lib/elkjs/elk.bundled.js`, copied unchanged from the npm package `elkjs@0.12.0`.
- Upstream: <https://github.com/kieler/elkjs>. The source code of this version is at <https://github.com/kieler/elkjs/tree/0.12.0>. It compiles the Java sources of the Eclipse Layout Kernel of the same minor version, at <https://github.com/eclipse/elk/tree/v0.12.0>.
- Licence: EPL-2.0 OR GPL-3.0-or-later. The text is in `IniaAi.WebApp/wwwroot/lib/elkjs/LICENSE.md`.

### Google "G" logo

- Files: `IniaAi.WebApp/wwwroot/images/google-g.svg`, on the "Sign in with Google" button.
- Terms: the logo is a trademark of Google LLC. The app shows it as Google's branding guidelines for Sign in with Google allow, at <https://developers.google.com/identity/branding-guidelines>. The licence of this repository does not cover it.

### Open Iconic 1.1.1

- Files: `IniaAi.WebApp/wwwroot/open-iconic/`
- Upstream: <https://github.com/iconic/open-iconic>
- Licences: MIT for the icons, OFL-1.1 for the font. The texts are in the same folder: `ICON-LICENSE` and `FONT-LICENSE`.

### PDFObject 2.3.0

- Files: `IniaAi.WebApp/wwwroot/PDFObject-2.3.0/`
- Upstream: <https://github.com/pipwerks/PDFObject>
- Licence: MIT. The text is in `IniaAi.WebApp/wwwroot/PDFObject-2.3.0/LICENSE.md`.

### Prompt editor bundle

- Files: `IniaAi.WebApp/wwwroot/js/prompt-editor.bundle.js`, built from `IniaAi.WebApp/wwwroot/js/src/`.
- Contents: CodeMirror and the npm packages it needs, from `IniaAi.WebApp/wwwroot/js/src/package-lock.json`. The package list below names each of them with its licence, with esbuild, which builds the bundle.

### QRCode.js

- Files: `IniaAi.WebApp/wwwroot/qrcode.min.js`, the same bytes as `qrcode.min.js` at commit `06c7a5e134f116402699f03cda5819e10a0e5787` of the upstream.
- Upstream: <https://github.com/davidshimjs/qrcodejs>
- Licence: MIT.

```text
The MIT License (MIT)

Copyright (c) 2012 davidshimjs

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### Unicode CLDR unit and currency data

- Files: `IniaAi.Core/Workflows/Annotations/Quantities/Resources/units_dictionary.json`, made from the CLDR data in Babel 2.18.0, and `unit_conversions.json`, which `generate_unit_conversions.py` in the same folder makes from CLDR `release-48-2`.
- Upstream: <https://github.com/unicode-org/cldr>, <https://github.com/python-babel/babel>
- Licence: Unicode-3.0.

```text
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 2004-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.

SPDX-License-Identifier: Unicode-3.0
```

<!-- generated by scripts/notices/notices.py: start -->
## Packages

Every package that the lock files and the package restore resolve, build and test tools included, with
the licence its metadata or a reviewed entry in `scripts/notices/policy.json` gives. A package appears once per
licence, without its version, so an update under the same licence leaves the list as it is. `python3 scripts/notices/notices.py write`
writes this list, and CI fails when it is out of date or a licence is outside the policy.

### NuGet packages (`artifacts/obj/*/project.assets.json`)

| Package | Licence |
| --- | --- |
| Amazon.AspNetCore.DataProtection.SSM | Apache-2.0 |
| Amazon.Extensions.Configuration.SystemsManager | Apache-2.0 |
| AngleSharp | MIT |
| AngleSharp.Css | MIT |
| AngleSharp.Diffing | MIT |
| Ardalis.Specification | MIT |
| Ardalis.Specification.EntityFrameworkCore | MIT |
| Asp.Versioning.Abstractions | MIT |
| Asp.Versioning.Http | MIT |
| Asp.Versioning.Mvc | MIT |
| Asp.Versioning.Mvc.ApiExplorer | MIT |
| AspNetCore.HealthChecks.UI.Client | Apache-2.0 |
| AspNetCore.HealthChecks.UI.Core | Apache-2.0 |
| AspNetCore.HealthChecks.Uris | Apache-2.0 |
| AutoFixture | MIT |
| AWSSDK.AppConfigData | Apache-2.0 |
| AWSSDK.BedrockRuntime | Apache-2.0 |
| AWSSDK.CloudWatch | Apache-2.0 |
| AWSSDK.Core | Apache-2.0 |
| AWSSDK.Extensions.CborProtocol | Apache-2.0 |
| AWSSDK.Extensions.NETCore.Setup | Apache-2.0 |
| AWSSDK.S3 | Apache-2.0 |
| AWSSDK.SecretsManager | Apache-2.0 |
| AWSSDK.ServiceQuotas | Apache-2.0 |
| AWSSDK.SimpleEmailV2 | Apache-2.0 |
| AWSSDK.SimpleSystemsManagement | Apache-2.0 |
| AWSSDK.SSO | Apache-2.0 |
| AWSSDK.SSOOIDC | Apache-2.0 |
| Azure.AI.OpenAI | MIT |
| Azure.Core | MIT |
| Azure.Extensions.AspNetCore.Configuration.Secrets | MIT |
| Azure.Extensions.AspNetCore.DataProtection.Keys | MIT |
| Azure.Identity | MIT |
| Azure.Monitor.OpenTelemetry.Exporter | MIT |
| Azure.Security.KeyVault.Keys | MIT |
| Azure.Security.KeyVault.Secrets | MIT |
| bblanchon.PDFium.Linux | Apache-2.0 |
| bblanchon.PDFium.macOS | Apache-2.0 |
| bblanchon.PDFium.Win32 | Apache-2.0 |
| Bogus | MIT AND BSD-3-Clause |
| BouncyCastle.Cryptography | MIT |
| bunit | MIT |
| Castle.Core | Apache-2.0 |
| ClosedXML | MIT |
| ClosedXML.Parser | MIT |
| CodeBeam.MudBlazor.Extensions | MIT |
| CommunityToolkit.HighPerformance | MIT |
| Corvus.Regex.Ecma262 | Apache-2.0 |
| Corvus.Text.Json | Apache-2.0 |
| Corvus.Text.Json.Validator | Apache-2.0 |
| CsvHelper | MS-PL OR Apache-2.0 |
| DocumentFormat.OpenXml | MIT |
| DocumentFormat.OpenXml.Framework | MIT |
| EFCore.NamingConventions | Apache-2.0 |
| ErrorOr | MIT |
| ExcelNumberFormat | MIT |
| ExtendedNumerics.BigDecimal | MIT |
| Fare | MIT |
| FileSignatures | MIT |
| Finbuckle.MultiTenant | Apache-2.0 |
| Finbuckle.MultiTenant.Abstractions | Apache-2.0 |
| Finbuckle.MultiTenant.AspNetCore | Apache-2.0 |
| Finbuckle.MultiTenant.EntityFrameworkCore | Apache-2.0 |
| FluentValidation | Apache-2.0 |
| FluentValidation.DependencyInjectionExtensions | Apache-2.0 |
| google-search-results-dotnet | MIT |
| Google.Apis | Apache-2.0 |
| Google.Apis.Auth | Apache-2.0 |
| Google.Apis.Core | Apache-2.0 |
| Google.Apis.CustomSearchAPI.v1 | Apache-2.0 |
| HtmlSanitizer | MIT |
| HtmlToOpenXml.dll | MIT |
| Humanizer.Core | MIT |
| ISO3166 | MIT |
| Markdig | BSD-2-Clause |
| Microsoft.ApplicationInsights | MIT |
| Microsoft.ApplicationInsights.AspNetCore | MIT |
| Microsoft.AspNetCore.App.Internal.Assets | MIT |
| Microsoft.AspNetCore.Authentication.Google | MIT |
| Microsoft.AspNetCore.Authentication.JwtBearer | MIT |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | MIT |
| Microsoft.AspNetCore.Authorization | MIT |
| Microsoft.AspNetCore.Components | MIT |
| Microsoft.AspNetCore.Components.Analyzers | MIT |
| Microsoft.AspNetCore.Components.Authorization | MIT |
| Microsoft.AspNetCore.Components.Forms | MIT |
| Microsoft.AspNetCore.Components.Web | MIT |
| Microsoft.AspNetCore.Components.WebAssembly | MIT |
| Microsoft.AspNetCore.Components.WebAssembly.Authentication | MIT |
| Microsoft.AspNetCore.Cryptography.Internal | MIT |
| Microsoft.AspNetCore.Cryptography.KeyDerivation | MIT |
| Microsoft.AspNetCore.DataProtection | MIT |
| Microsoft.AspNetCore.DataProtection.Abstractions | MIT |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | MIT |
| Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore | MIT |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | MIT |
| Microsoft.AspNetCore.Metadata | MIT |
| Microsoft.AspNetCore.Mvc.Testing | MIT |
| Microsoft.AspNetCore.OData | MIT |
| Microsoft.AspNetCore.TestHost | MIT |
| Microsoft.Bcl.AsyncInterfaces | MIT |
| Microsoft.Bcl.Cryptography | MIT |
| Microsoft.Bcl.HashCode | MIT |
| Microsoft.Build.Framework | MIT |
| Microsoft.CodeAnalysis.Analyzers | MIT |
| Microsoft.CodeAnalysis.Common | MIT |
| Microsoft.CodeAnalysis.CSharp | MIT |
| Microsoft.CodeAnalysis.CSharp.Workspaces | MIT |
| Microsoft.CodeAnalysis.Workspaces.Common | MIT |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | MIT |
| Microsoft.CodeCoverage | MIT |
| Microsoft.Data.SqlClient | MIT |
| Microsoft.Data.SqlClient.SNI.runtime | LicenseRef-Microsoft-SqlClient-SNI |
| Microsoft.Data.Sqlite.Core | MIT |
| Microsoft.EntityFrameworkCore | MIT |
| Microsoft.EntityFrameworkCore.Abstractions | MIT |
| Microsoft.EntityFrameworkCore.Analyzers | MIT |
| Microsoft.EntityFrameworkCore.Design | MIT |
| Microsoft.EntityFrameworkCore.InMemory | MIT |
| Microsoft.EntityFrameworkCore.Relational | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | MIT |
| Microsoft.EntityFrameworkCore.Sqlite.Core | MIT |
| Microsoft.EntityFrameworkCore.SqlServer | MIT |
| Microsoft.EntityFrameworkCore.Tools | MIT |
| Microsoft.Extensions.AI | MIT |
| Microsoft.Extensions.AI.Abstractions | MIT |
| Microsoft.Extensions.AI.OpenAI | MIT |
| Microsoft.Extensions.ApiDescription.Server | MIT |
| Microsoft.Extensions.Caching.Abstractions | MIT |
| Microsoft.Extensions.Caching.Memory | MIT |
| Microsoft.Extensions.Configuration | MIT |
| Microsoft.Extensions.Configuration.Abstractions | MIT |
| Microsoft.Extensions.Configuration.Binder | MIT |
| Microsoft.Extensions.Configuration.CommandLine | MIT |
| Microsoft.Extensions.Configuration.EnvironmentVariables | MIT |
| Microsoft.Extensions.Configuration.FileExtensions | MIT |
| Microsoft.Extensions.Configuration.Json | MIT |
| Microsoft.Extensions.Configuration.KeyPerFile | MIT |
| Microsoft.Extensions.Configuration.UserSecrets | MIT |
| Microsoft.Extensions.DependencyInjection | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT |
| Microsoft.Extensions.DependencyModel | MIT |
| Microsoft.Extensions.Diagnostics | MIT |
| Microsoft.Extensions.Diagnostics.Abstractions | MIT |
| Microsoft.Extensions.Diagnostics.HealthChecks | MIT |
| Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions | MIT |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | MIT |
| Microsoft.Extensions.FileProviders.Abstractions | MIT |
| Microsoft.Extensions.FileProviders.Physical | MIT |
| Microsoft.Extensions.FileSystemGlobbing | MIT |
| Microsoft.Extensions.Hosting | MIT |
| Microsoft.Extensions.Hosting.Abstractions | MIT |
| Microsoft.Extensions.Http | MIT |
| Microsoft.Extensions.Identity.Core | MIT |
| Microsoft.Extensions.Identity.Stores | MIT |
| Microsoft.Extensions.Localization | MIT |
| Microsoft.Extensions.Localization.Abstractions | MIT |
| Microsoft.Extensions.Logging | MIT |
| Microsoft.Extensions.Logging.Abstractions | MIT |
| Microsoft.Extensions.Logging.Configuration | MIT |
| Microsoft.Extensions.Logging.Console | MIT |
| Microsoft.Extensions.Logging.Debug | MIT |
| Microsoft.Extensions.Logging.EventLog | MIT |
| Microsoft.Extensions.Logging.EventSource | MIT |
| Microsoft.Extensions.ObjectPool | MIT |
| Microsoft.Extensions.Options | MIT |
| Microsoft.Extensions.Options.ConfigurationExtensions | MIT |
| Microsoft.Extensions.Primitives | MIT |
| Microsoft.Extensions.Validation | MIT |
| Microsoft.Extensions.VectorData.Abstractions | MIT |
| Microsoft.Identity.Client | MIT |
| Microsoft.Identity.Client.Broker | MIT |
| Microsoft.Identity.Client.Extensions.Msal | MIT |
| Microsoft.Identity.Client.NativeInterop | LicenseRef-Microsoft-Identity-Client-NativeInterop |
| Microsoft.IdentityModel.Abstractions | MIT |
| Microsoft.IdentityModel.JsonWebTokens | MIT |
| Microsoft.IdentityModel.Logging | MIT |
| Microsoft.IdentityModel.Protocols | MIT |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | MIT |
| Microsoft.IdentityModel.Tokens | MIT |
| Microsoft.JSInterop | MIT |
| Microsoft.JSInterop.WebAssembly | MIT |
| Microsoft.NET.Test.Sdk | MIT |
| Microsoft.NETCore.Platforms | LicenseRef-Microsoft-DotNet-Library |
| Microsoft.OData.Core | MIT |
| Microsoft.OData.Edm | MIT |
| Microsoft.OData.ModelBuilder | MIT |
| Microsoft.OpenApi | MIT |
| Microsoft.SemanticKernel | MIT |
| Microsoft.SemanticKernel.Abstractions | MIT |
| Microsoft.SemanticKernel.Connectors.AzureOpenAI | MIT |
| Microsoft.SemanticKernel.Connectors.OpenAI | MIT |
| Microsoft.SemanticKernel.Core | MIT |
| Microsoft.Spatial | MIT |
| Microsoft.SqlServer.Server | MIT |
| Microsoft.Testing.Extensions.Telemetry | MIT |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | MIT |
| Microsoft.Testing.Platform | MIT |
| Microsoft.Testing.Platform.MSBuild | MIT |
| Microsoft.TestPlatform.ObjectModel | MIT |
| Microsoft.TestPlatform.TestHost | MIT |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | LicenseRef-Microsoft-Visual-Studio-Container-Tools |
| Microsoft.VisualStudio.SolutionPersistence | MIT |
| Microsoft.Win32.Registry | MIT |
| MimeKit | MIT |
| Minio | Apache-2.0 |
| Mono.TextTemplating | MIT |
| Moq | BSD-3-Clause |
| MudBlazor | MIT |
| MudBlazor.Markdown | MIT |
| NCalc.Core | MIT |
| NCalc.Domain | MIT |
| NCalc.Parser | MIT |
| NCalcSync | MIT |
| NETStandard.Library | LicenseRef-Microsoft-DotNet-Library |
| Newtonsoft.Json | MIT |
| NodaTime | Apache-2.0 |
| Npgsql | PostgreSQL |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL |
| NReco.Text.AhoCorasickDoubleArrayTrie | Apache-2.0 |
| NSubstitute | BSD-3-Clause |
| OpenAI | MIT |
| OpenIddict.Abstractions | Apache-2.0 |
| OpenIddict.Client | Apache-2.0 |
| OpenIddict.Core | Apache-2.0 |
| OpenIddict.EntityFrameworkCore | Apache-2.0 |
| OpenIddict.EntityFrameworkCore.Models | Apache-2.0 |
| OpenIddict.Server | Apache-2.0 |
| OpenIddict.Server.AspNetCore | Apache-2.0 |
| OpenIddict.Validation | Apache-2.0 |
| OpenIddict.Validation.AspNetCore | Apache-2.0 |
| OpenIddict.Validation.ServerIntegration | Apache-2.0 |
| OpenMcdf | MPL-2.0 |
| OpenTelemetry | Apache-2.0 |
| OpenTelemetry.Api | Apache-2.0 |
| OpenTelemetry.Api.ProviderBuilderExtensions | Apache-2.0 |
| OpenTelemetry.Extensions.Hosting | Apache-2.0 |
| OpenTelemetry.Instrumentation.AspNetCore | Apache-2.0 |
| OpenTelemetry.Instrumentation.Http | Apache-2.0 |
| OpenTelemetry.Instrumentation.SqlClient | Apache-2.0 |
| OpenTelemetry.PersistentStorage.Abstractions | Apache-2.0 |
| OpenTelemetry.PersistentStorage.FileSystem | Apache-2.0 |
| Parlot | BSD-3-Clause |
| Polly | BSD-3-Clause |
| Polly.Core | BSD-3-Clause |
| RBush.Signed | MIT |
| Selenium.WebDriver | Apache-2.0 |
| SendGrid | MIT |
| Serilog | Apache-2.0 |
| Serilog.Extensions.Logging | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.Debug | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |
| SharpToken | MIT |
| SixLabors.Fonts | Apache-2.0 |
| SkiaSharp | MIT |
| SkiaSharp.NativeAssets.Linux.NoDependencies | MIT |
| SkiaSharp.NativeAssets.macOS | MIT |
| SkiaSharp.NativeAssets.Win32 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | Apache-2.0 |
| SQLitePCLRaw.core | Apache-2.0 |
| SQLitePCLRaw.lib.e_sqlite3 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | Apache-2.0 |
| starkbank-ecdsa | MIT |
| Swashbuckle.AspNetCore | MIT |
| Swashbuckle.AspNetCore.Swagger | MIT |
| Swashbuckle.AspNetCore.SwaggerGen | MIT |
| Swashbuckle.AspNetCore.SwaggerUI | MIT |
| System.ClientModel | MIT |
| System.CodeDom | MIT |
| System.ComponentModel.Annotations | MIT |
| System.Composition | MIT |
| System.Composition.AttributedModel | MIT |
| System.Composition.Convention | MIT |
| System.Composition.Hosting | MIT |
| System.Composition.Runtime | MIT |
| System.Composition.TypedParts | MIT |
| System.Configuration.ConfigurationManager | MIT |
| System.Diagnostics.EventLog | MIT |
| System.Formats.Cbor | MIT |
| System.IdentityModel.Tokens.Jwt | MIT |
| System.IO.Hashing | MIT |
| System.IO.Packaging | MIT |
| System.Management | MIT |
| System.Memory.Data | MIT |
| System.Numerics.Tensors | MIT |
| System.Reactive | MIT |
| System.Runtime.Caching | MIT |
| System.Security.Cryptography.Pkcs | MIT |
| System.Security.Cryptography.ProtectedData | MIT |
| System.Security.Cryptography.Xml | MIT |
| xunit.analyzers | Apache-2.0 |
| xunit.v3 | Apache-2.0 |
| xunit.v3.assert | Apache-2.0 |
| xunit.v3.common | Apache-2.0 |
| xunit.v3.core.mtp-v1 | Apache-2.0 |
| xunit.v3.extensibility.core | Apache-2.0 |
| xunit.v3.mtp-v1 | Apache-2.0 |
| xunit.v3.runner.common | Apache-2.0 |
| xunit.v3.runner.inproc.console | Apache-2.0 |

### npm packages (`IniaAi.WebApp/wwwroot/js/src/package-lock.json`)

| Package | Licence |
| --- | --- |
| @codemirror/autocomplete | MIT |
| @codemirror/commands | MIT |
| @codemirror/language | MIT |
| @codemirror/state | MIT |
| @codemirror/view | MIT |
| @esbuild/aix-ppc64 | MIT |
| @esbuild/android-arm | MIT |
| @esbuild/android-arm64 | MIT |
| @esbuild/android-x64 | MIT |
| @esbuild/darwin-arm64 | MIT |
| @esbuild/darwin-x64 | MIT |
| @esbuild/freebsd-arm64 | MIT |
| @esbuild/freebsd-x64 | MIT |
| @esbuild/linux-arm | MIT |
| @esbuild/linux-arm64 | MIT |
| @esbuild/linux-ia32 | MIT |
| @esbuild/linux-loong64 | MIT |
| @esbuild/linux-mips64el | MIT |
| @esbuild/linux-ppc64 | MIT |
| @esbuild/linux-riscv64 | MIT |
| @esbuild/linux-s390x | MIT |
| @esbuild/linux-x64 | MIT |
| @esbuild/netbsd-arm64 | MIT |
| @esbuild/netbsd-x64 | MIT |
| @esbuild/openbsd-arm64 | MIT |
| @esbuild/openbsd-x64 | MIT |
| @esbuild/openharmony-arm64 | MIT |
| @esbuild/sunos-x64 | MIT |
| @esbuild/win32-arm64 | MIT |
| @esbuild/win32-ia32 | MIT |
| @esbuild/win32-x64 | MIT |
| @lezer/common | MIT |
| @lezer/highlight | MIT |
| @lezer/lr | MIT |
| @marijn/find-cluster-break | MIT |
| crelt | MIT |
| esbuild | MIT |
| style-mod | MIT |
| w3c-keyname | MIT |

### Terms outside the allowed list

These packages are under terms that are not on the allowed list. Their terms follow in full.

#### Microsoft.Data.SqlClient.SNI.runtime 6.0.3

```text
MICROSOFT SOFTWARE LICENSE TERMS

MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY

These license terms are an agreement between you and Microsoft Corporation (or based on where you live, one of its affiliates). They apply to the software named above. The terms also apply to any Microsoft services or updates for the software, except to the extent those have different terms.

IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW.

1.  INSTALLATION AND USE RIGHTS.
    You may install and use any number of copies of the software to develop and test your applications. 
2.  THIRD PARTY COMPONENTS. The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software.
3.  ADDITIONAL LICENSING REQUIREMENTS AND/OR USE RIGHTS.
    a. DISTRIBUTABLE CODE.  The software is comprised of Distributable Code. "Distributable Code" is code that you are permitted to distribute in applications you develop if you comply with the terms below.
       i. Right to Use and Distribute.
          * You may copy and distribute the object code form of the software.
          * Third Party Distribution. You may permit distributors of your applications to copy and distribute the Distributable Code as part of those applications.
      ii. Distribution Requirements. For any Distributable Code you distribute, you must
          * use the Distributable Code in your applications and not as a standalone distribution;
          * require distributors and external end users to agree to terms that protect it at least as much as this agreement; and
          * indemnify, defend, and hold harmless Microsoft from any claims, including attorneys' fees, related to the distribution or use of your applications, except to the extent that any claim is based solely on the unmodified Distributable Code.
     iii. Distribution Restrictions. You may not
          * use Microsoft's trademarks in your applications' names or in a way that suggests your applications come from or are endorsed by Microsoft; or
          * modify or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. An "Excluded License" is one that requires, as a condition of use, modification or distribution of code, that (i) it be disclosed or distributed in source code form; or (ii) others have the right to modify it.
4.  DATA.
    a. Data Collection. Some features in the software may enable collection of data from users of your applications that access or use the software. If you use these features to enable data collection in your applications, you must comply with applicable law, including getting any required user consent, and maintain a prominent privacy policy that accurately informs users about how you use, collect, and share their data. You agree to comply with all applicable provisions of the Microsoft Privacy Statement at [https://go.microsoft.com/fwlink/?LinkId=521839].
5.  SCOPE OF LICENSE. The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. You may not
    * work around any technical limitations in the software;
    * reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source code for the software, except and to the extent required by third party licensing terms governing use of certain open source components that may be included in the software;
    * remove, minimize, block or modify any notices of Microsoft or its suppliers in the software;
    * use the software in any way that is against the law; or
    * share, publish, rent or lease the software, provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.
6.  EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit www.microsoft.com/exporting.  
7.  SUPPORT SERVICES. Because this software is "as is," we may not provide support services for it.
8.  ENTIRE AGREEMENT. This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.
9.  APPLICABLE LAW.  If you acquired the software in the United States, Washington law applies to interpretation of and claims for breach of this agreement, and the laws of the state where you live apply to all other claims. If you acquired the software in any other country, its laws apply.
10. CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:
    a) Australia. You have statutory guarantees under the Australian Consumer Law and nothing in this agreement is intended to affect those rights.
    b) Canada. If you acquired this software in Canada, you may stop receiving updates by turning off the automatic update feature, disconnecting your device from the Internet (if and when you re-connect to the Internet, however, the software will resume checking for and installing updates), or uninstalling the software. The product documentation, if any, may also specify how to turn off updates for your specific device or software.
    c) Germany and Austria.
       (i) Warranty. The software will perform substantially as described in any Microsoft materials that accompany it. However, Microsoft gives no contractual guarantee in relation to the software.
       (ii) Limitation of Liability. In case of intentional conduct, gross negligence, claims based on the Product Liability Act, as well as in case of death or personal or physical injury, Microsoft is liable according to the statutory law.
    Subject to the foregoing clause (ii), Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence
11. DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED "AS-IS." YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.
12. LIMITATION ON AND EXCLUSION OF REMEDIES AND DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.
    This limitation applies to (a) anything related to the software, services, content (including code) on third party Internet sites, or third party applications; and (b) claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.
    It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your state or country may not allow the exclusion or limitation of incidental, consequential or other damages.
```

#### Microsoft.Identity.Client.NativeInterop 0.20.6

```text
MICROSOFT SOFTWARE LICENSE TERMS

IF YOU LIVE IN (OR ARE A BUSINESS WITH A PRINCIPAL PLACE OF BUSINESS IN) THE UNITED
STATES, PLEASE READ THE “BINDING ARBITRATION AND CLASS ACTION WAIVER” SECTION
BELOW. IT AFFECTS HOW DISPUTES ARE RESOLVED.
These license terms are an agreement between you and Microsoft Corporation (or one of its affiliates). They
apply to the software named above and any Microsoft services or software updates (except to the extent such
services or updates are accompanied by new or additional terms, in which case those different terms apply
prospectively and do not alter your or Microsoft’s rights relating to pre-updated software or services). IF YOU
COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW. BY USING THE SOFTWARE, YOU
ACCEPT THESE TERMS.

1. INSTALLATION AND USE RIGHTS.
a) General. You may install and use any number of copies of the software.
b) Work or School Accounts. You can sign into the software with a work or school email address. If

you do, you agree that the owner of the domain associated with your email address may control and
administer your account, and access and process your data, including the contents of your
communications and files. You further agree that your use of the software may be subject to: i) your
organization’s guidelines and policies regarding the use of the software; and ii) the agreements
Microsoft has with you or your organization, and in such case these terms may not apply. If you
already have a Microsoft account and you use a separate work or school email address to access the
software, you may be prompted to update the email address associated with your Microsoft account to
continue accessing the software.

c) Third Party Components. The software may include third party components with separate legal
notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s)
accompanying the software.

d) Microsoft Services Agreement. Some features of the software provide access to, or rely on, online
services. The use of those services (but not the software) is governed by the separate terms and
privacy policies in the Microsoft Services Agreement at
https://go.microsoft.com/fwlink/?linkid=398923. Please read them. The services may not be available
in all regions.

2. DATA.

a) Data Collection. The software may collect information about you and your use of the software, and

send that to Microsoft. Microsoft may use this information to provide services and improve our
products and services. You may opt-out of many of these scenarios, but not all, as described in the
product documentation. There are also some features in the software that may enable you to collect
data from users of your applications. If you use these features to enable data collection in your
applications, you must comply with applicable law, including providing appropriate notices to users of
your applications. You can learn more about data collection and use in the help documentation and
the privacy statement at https://aka.ms/privacy. Your use of the software operates as your consent to
these practices.

b) Processing of Personal Data. To the extent Microsoft is a processor or subprocessor of personal

data in connection with the software, Microsoft makes the commitments in the European Union
General Data Protection Regulation Terms of the Online Services Terms to all customers effective May
25, 2018, at https://docs.microsoft.com/en-us/legal/gdpr.

3. SCOPE OF LICENSE. The software is licensed, not sold. Microsoft reserves all other rights. Unless
applicable law gives you more rights despite this limitation, you will not (and have no right to):

a) work around any technical limitations in the software that only allow you to use it in certain ways;
b) reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source
code for the software, except and to the extent required by third party licensing terms governing use
of certain open source components that may be included in the software;
c) remove, minimize, block, or modify any notices of Microsoft or its suppliers in the software;
d) use the software in any way that is against the law or to create or propagate malware; or
e) share, publish, distribute, or lease the software, provide the software as a stand-alone offering for
others to use, or transfer the software or this agreement to any third party.

4. EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and

regulations that apply to the software, which include restrictions on destinations, end users, and end use.
For further information on export restrictions, visit https://aka.ms/exporting.

5. SUPPORT SERVICES. Microsoft is not obligated under this agreement to provide any support services for
the software. Any support provided is “as is”, “with all faults”, and without warranty of any kind.

6. UPDATES. The software may periodically check for updates, and download and install them for you. You

may obtain updates only from Microsoft or authorized sources. Microsoft may need to update your system
to provide you with updates. You agree to receive these automatic updates without any additional notice.
Updates may not include or support all existing software features, services, or peripheral devices.

7. BINDING ARBITRATION AND CLASS ACTION WAIVER. This Section applies if you live in (or, if
a business, your principal place of business is in) the United States. If you and Microsoft have a
dispute, you and Microsoft agree to try for 60 days to resolve it informally. If you and Microsoft can’t, you
and Microsoft agree to binding individual arbitration before the American Arbitration
Association under the Federal Arbitration Act (“FAA”), and not to sue in court in front of a judge or
jury. Instead, a neutral arbitrator will decide. Class action lawsuits, class-wide arbitrations, private
attorney-general actions, and any other proceeding where someone acts in a representative
capacity are not allowed; nor is combining individual proceedings without the consent of all
parties. The complete Arbitration Agreement contains more terms and is at https://aka.ms/arbagreement-4. You and Microsoft agree to these terms.

8. ENTIRE AGREEMENT. This agreement, and any other terms Microsoft may provide for supplements,
updates, or third-party applications, is the entire agreement for the software.

9. APPLICABLE LAW AND PLACE TO RESOLVE DISPUTES. If you acquired the software in the United

States or Canada, the laws of the state or province where you live (or, if a business, where your principal
place of business is located) govern the interpretation of this agreement, claims for its breach, and all
other claims (including consumer protection, unfair competition, and tort claims), regardless of conflict of
laws principles, except that the FAA governs everything related to arbitration. If you acquired the software
in any other country, its laws apply, except that the FAA governs everything related to arbitration. If U.S.
federal jurisdiction exists, you and Microsoft consent to exclusive jurisdiction and venue in the federal court
in King County, Washington for all disputes heard in court (excluding arbitration). If not, you and Microsoft
consent to exclusive jurisdiction and venue in the Superior Court of King County, Washington for all
disputes heard in court (excluding arbitration).

10. CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You
may have other rights, including consumer rights, under the laws of your state, province, or country.
Separate and apart from your relationship with Microsoft, you may also have rights with respect to the
party from which you acquired the software. This agreement does not change those other rights if the
laws of your state, province, or country do not permit it to do so. For example, if you acquired the
software in one of the below regions, or mandatory country law applies, then the following provisions
apply to you:

a) Australia. You have statutory guarantees under the Australian Consumer Law and nothing in this
agreement is intended to affect those rights.
b) Canada. If you acquired this software in Canada, you may stop receiving updates by turning off the
automatic update feature, disconnecting your device from the Internet (if and when you re-connect to
the Internet, however, the software will resume checking for and installing updates), or uninstalling
the software. The product documentation, if any, may also specify how to turn off updates for your
specific device or software.
c) Germany and Austria.
i.

Warranty. The properly licensed software will perform substantially as described in any Microsoft
materials that accompany the software. However, Microsoft gives no contractual guarantee in
relation to the licensed software.

ii. Limitation of Liability. In case of intentional conduct, gross negligence, claims based on the
Product Liability Act, as well as, in case of death or personal or physical injury, Microsoft is liable
according to the statutory law.
Subject to the foregoing clause ii., Microsoft will only be liable for slight negligence if Microsoft is in
breach of such material contractual obligations, the fulfillment of which facilitate the due performance
of this agreement, the breach of which would endanger the purpose of this agreement and the
compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases
of slight negligence, Microsoft will not be liable for slight negligence.

11. DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED “AS IS.” YOU BEAR THE RISK OF

USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES, OR CONDITIONS.
TO THE EXTENT PERMITTED UNDER APPLICABLE LAWS, MICROSOFT EXCLUDES ALL IMPLIED
WARRANTIES, INCLUDING MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND
NON-INFRINGEMENT.

12. LIMITATION ON AND EXCLUSION OF DAMAGES. IF YOU HAVE ANY BASIS FOR RECOVERING

DAMAGES DESPITE THE PRECEDING DISCLAIMER OF WARRANTY, YOU CAN RECOVER FROM
MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT
RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL,
INDIRECT OR INCIDENTAL DAMAGES.
This limitation applies to (a) anything related to the software, services, content (including
code) on third party Internet sites, or third party applications; and (b) claims for breach of
contract, warranty, guarantee, or condition; strict liability, negligence, or other tort; or any
other claim; in each case to the extent permitted by applicable law.
It also applies even if Microsoft knew or should have known about the possibility of the
damages. The above limitation or exclusion may not apply to you because your state,
province, or country may not allow the exclusion or limitation of incidental, consequential, or
other damages.

Please note: As this software is distributed in Canada, some of the clauses in this agreement are
provided below in French.
Remarque: Ce logiciel étant distribué au Canada, certaines des clauses dans ce contrat sont
fournies ci-dessous en français.
EXONÉRATION DE GARANTIE. Le logiciel visé par une licence est offert « tel quel ». Toute
utilisation de ce logiciel est à votre seule risque et péril. Microsoft n’accorde aucune autre
garantie expresse. Vous pouvez bénéficier de droits additionnels en vertu du droit local sur la
protection des consommateurs, que ce contrat ne peut modifier. La ou elles sont permises par le
droit locale, les garanties implicites de qualité marchande, d’adéquation à un usage particulier et
d’absence de contrefaçon sont exclues.

LIMITATION DES DOMMAGES-INTÉRÊTS ET EXCLUSION DE RESPONSABILITÉ POUR LES
DOMMAGES. Vous pouvez obtenir de Microsoft et de ses fournisseurs une indemnisation en cas de
dommages directs uniquement à hauteur de 5,00 $ US. Vous ne pouvez prétendre à aucune
indemnisation pour les autres dommages, y compris les dommages spéciaux, indirects ou
accessoires et pertes de bénéfices.
Cette limitation concerne:
•

tout ce qui est relié au logiciel, aux services ou au contenu (y compris le code) figurant sur
des sites Internet tiers ou dans des programmes tiers; et

•

les réclamations au titre de violation de contrat ou de garantie, ou au titre de responsabilité
stricte, de négligence ou d’une autre faute dans la limite autorisée par la loi en vigueur.

Elle s’applique également, même si Microsoft connaissait ou devrait connaître l’éventualité d’un
tel dommage. Si votre pays n’autorise pas l’exclusion ou la limitation de responsabilité pour les
dommages indirects, accessoires ou de quelque nature que ce soit, il se peut que la limitation ou
l’exclusion ci-dessus ne s’appliquera pas à votre égard.
EFFET JURIDIQUE. Le présent contrat décrit certains droits juridiques. Vous pourriez avoir
d’autres droits prévus par les lois de votre pays. Le présent contrat ne modifie pas les droits que
vous confèrent les lois de votre pays si celles-ci ne le permettent pas.
```

#### Microsoft.NETCore.Platforms 1.1.0, NETStandard.Library 1.6.1

```text

MICROSOFT SOFTWARE LICENSE TERMS


MICROSOFT .NET LIBRARY 

These license terms are an agreement between Microsoft Corporation (or based on where you live, one of its affiliates) and you. Please read them. They apply to the software named above, which includes the media on which you received it, if any. The terms also apply to any Microsoft

·         updates,

·         supplements,

·         Internet-based services, and

·         support services

for this software, unless other terms accompany those items. If so, those terms apply.

BY USING THE SOFTWARE, YOU ACCEPT THESE TERMS. IF YOU DO NOT ACCEPT THEM, DO NOT USE THE SOFTWARE.


IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE PERPETUAL RIGHTS BELOW.

1.    INSTALLATION AND USE RIGHTS. 

a.    Installation and Use. You may install and use any number of copies of the software to design, develop and test your programs.

b.    Third Party Programs. The software may include third party programs that Microsoft, not the third party, licenses to you under this agreement. Notices, if any, for the third party program are included for your information only.

2.    ADDITIONAL LICENSING REQUIREMENTS AND/OR USE RIGHTS.

a.    DISTRIBUTABLE CODE.  The software is comprised of Distributable Code. “Distributable Code” is code that you are permitted to distribute in programs you develop if you comply with the terms below.

i.      Right to Use and Distribute. 

·         You may copy and distribute the object code form of the software.

·         Third Party Distribution. You may permit distributors of your programs to copy and distribute the Distributable Code as part of those programs.

ii.    Distribution Requirements. For any Distributable Code you distribute, you must

·         add significant primary functionality to it in your programs;

·         require distributors and external end users to agree to terms that protect it at least as much as this agreement;

·         display your valid copyright notice on your programs; and

·         indemnify, defend, and hold harmless Microsoft from any claims, including attorneys’ fees, related to the distribution or use of your programs.

iii.   Distribution Restrictions. You may not

·         alter any copyright, trademark or patent notice in the Distributable Code;

·         use Microsoft’s trademarks in your programs’ names or in a way that suggests your programs come from or are endorsed by Microsoft;

·         include Distributable Code in malicious, deceptive or unlawful programs; or

·         modify or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. An Excluded License is one that requires, as a condition of use, modification or distribution, that

·         the code be disclosed or distributed in source code form; or

·         others have the right to modify it.

3.    SCOPE OF LICENSE. The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. You may not

·         work around any technical limitations in the software;

·         reverse engineer, decompile or disassemble the software, except and only to the extent that applicable law expressly permits, despite this limitation;

·         publish the software for others to copy;

·         rent, lease or lend the software;

·         transfer the software or this agreement to any third party; or

·         use the software for commercial software hosting services.

4.    BACKUP COPY. You may make one backup copy of the software. You may use it only to reinstall the software.

5.    DOCUMENTATION. Any person that has valid access to your computer or internal network may copy and use the documentation for your internal, reference purposes.

6.    EXPORT RESTRICTIONS. The software is subject to United States export laws and regulations. You must comply with all domestic and international export laws and regulations that apply to the software. These laws include restrictions on destinations, end users and end use. For additional information, see www.microsoft.com/exporting.

7.    SUPPORT SERVICES. Because this software is “as is,” we may not provide support services for it.

8.    ENTIRE AGREEMENT. This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.

9.    APPLICABLE LAW.

a.    United States. If you acquired the software in the United States, Washington state law governs the interpretation of this agreement and applies to claims for breach of it, regardless of conflict of laws principles. The laws of the state where you live govern all other claims, including claims under state consumer protection laws, unfair competition laws, and in tort.

b.    Outside the United States. If you acquired the software in any other country, the laws of that country apply.

10.  LEGAL EFFECT. This agreement describes certain legal rights. You may have other rights under the laws of your country. You may also have rights with respect to the party from whom you acquired the software. This agreement does not change your rights under the laws of your country if the laws of your country do not permit it to do so.

11.  DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED “AS-IS.” YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. YOU MAY HAVE ADDITIONAL CONSUMER RIGHTS OR STATUTORY GUARANTEES UNDER YOUR LOCAL LAWS WHICH THIS AGREEMENT CANNOT CHANGE. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.

FOR AUSTRALIA – YOU HAVE STATUTORY GUARANTEES UNDER THE AUSTRALIAN CONSUMER LAW AND NOTHING IN THESE TERMS IS INTENDED TO AFFECT THOSE RIGHTS.

12.  LIMITATION ON AND EXCLUSION OF REMEDIES AND DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.

This limitation applies to

·         anything related to the software, services, content (including code) on third party Internet sites, or third party programs; and

·         claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.

It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your country may not allow the exclusion or limitation of incidental, consequential or other damages.

Please note: As this software is distributed in Quebec, Canada, some of the clauses in this agreement are provided below in French.

Remarque : Ce logiciel étant distribué au Québec, Canada, certaines des clauses dans ce contrat sont fournies ci-dessous en français.

EXONÉRATION DE GARANTIE. Le logiciel visé par une licence est offert « tel quel ». Toute utilisation de ce logiciel est à votre seule risque et péril. Microsoft n’accorde aucune autre garantie expresse. Vous pouvez bénéficier de droits additionnels en vertu du droit local sur la protection des consommateurs, que ce contrat ne peut modifier. La ou elles sont permises par le droit locale, les garanties implicites de qualité marchande, d’adéquation à un usage particulier et d’absence de contrefaçon sont exclues.

LIMITATION DES DOMMAGES-INTÉRÊTS ET EXCLUSION DE RESPONSABILITÉ POUR LES DOMMAGES. Vous pouvez obtenir de Microsoft et de ses fournisseurs une indemnisation en cas de dommages directs uniquement à hauteur de 5,00 $ US. Vous ne pouvez prétendre à aucune indemnisation pour les autres dommages, y compris les dommages spéciaux, indirects ou accessoires et pertes de bénéfices.

Cette limitation concerne :

·         tout ce qui est relié au logiciel, aux services ou au contenu (y compris le code) figurant sur des sites Internet tiers ou dans des programmes tiers ; et

·         les réclamations au titre de violation de contrat ou de garantie, ou au titre de responsabilité stricte, de négligence ou d’une autre faute dans la limite autorisée par la loi en vigueur.

Elle s’applique également, même si Microsoft connaissait ou devrait connaître l’éventualité d’un tel dommage. Si votre pays n’autorise pas l’exclusion ou la limitation de responsabilité pour les dommages indirects, accessoires ou de quelque nature que ce soit, il se peut que la limitation ou l’exclusion ci-dessus ne s’appliquera pas à votre égard.

EFFET JURIDIQUE. Le présent contrat décrit certains droits juridiques. Vous pourriez avoir d’autres droits prévus par les lois de votre pays. Le présent contrat ne modifie pas les droits que vous confèrent les lois de votre pays si celles-ci ne le permettent pas.
```

#### Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.23.0

```text
## MICROSOFT SOFTWARE LICENSE TERMS
## MICROSOFT VISUAL STUDIO CONTAINER TOOLS TARGETS
---
These license terms are an agreement between Microsoft Corporation (or based on where you live, one of its affiliates) and you. They apply to the software named above. The terms also apply to any Microsoft services or updates for the software, except to the extent those have additional terms.

---
**IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW.**
1. **INSTALLATION AND USE RIGHTS.** You may install and use any number of copies of the software to develop and test your applications.
2. **TERMS FOR SPECIFIC COMPONENTS.**
    * **Utilities.** The software may contain some items on the Utilities List at https://go.microsoft.com/fwlink/?linkid=823097.  You may copy and install these Utilities, if included with the software, onto devices to debug and deploy your applications and databases you developed with the software. Please note that Utilities are designed for temporary use, that Microsoft may not be able to patch or update Utilities separately from the rest of the software, and that some Utilities by their nature may make it possible for others to access devices on which the Utilities are installed. As a result, you should delete all Utilities you have installed after you finish debugging or deploying your applications and databases.  Microsoft is not responsible for any third party use or access of Utilities you install on any device.
    * **Build Tools.** The software may include build tools which have specific use terms.   For build tools, you may copy and install files from the software onto your build devices, including physical devices and virtual machines or containers on those machines, whether on-premises or remote machines that are owned by you, hosted on Azure for you, or dedicated solely to your use (collectively, “Build Devices”).  You and others in your organization may use these files on your Build Devices solely to compile, build, and verify applications or run quality or performance tests of those applications as part of the build process.  For clarity, “applications” means applications developed by you and others in your organization who are each licensed to use the software.
    * **Microsoft Platforms.**  The software may include components from Microsoft Windows; Microsoft Windows Server; Microsoft SQL Server; Microsoft Exchange; Microsoft Office; and Microsoft SharePoint. These components are governed by separate agreements and their own product support policies, as described in the Microsoft “Licenses” folder accompanying the software, except that, if license terms for those components are also included in the associated installation directory, those license terms control.
    * **Third Party Components.**  The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software. 
3. **DATA.**
    * **Data Collection.** The software may collect information about you and your use of the software, and send that to Microsoft. Microsoft may use this information to provide services and improve our products and services.  You may opt-out of many of these scenarios, but not all, as described in the product documentation.  There are also some features in the software that may enable you and Microsoft to collect data from users of your applications. If you use these features, you must comply with applicable law, including providing appropriate notices to users of your applications together with a copy of Microsoft’s privacy statement. Our privacy statement is located at https://go.microsoft.com/fwlink/?LinkID=824704. You can learn more about data collection and use in the help documentation and our privacy statement. Your use of the software operates as your consent to these practices.
    * **Processing of Personal Data.** To the extent Microsoft is a processor or subprocessor of personal data in connection with the software, Microsoft makes the commitments in the European Union General Data Protection Regulation Terms of the Online Services Terms to all customers effective May 25, 2018, at http://go.microsoft.com/?linkid=9840733. 
4. **SCOPE OF LICENSE.** The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. For more information, see www.microsoft.com/licensing/userights. You may not
    * work around any technical limitations in the software;
    * reverse engineer, decompile or disassemble the software, or attempt to do so, except and only to the extent required by third party licensing terms governing use of certain open-source components that may be included with the software;
    * remove, minimize, block or modify any notices of Microsoft or its suppliers in the software; 
    * use the software in any way that is against the law; or
    * share, publish, rent, or lease the software, or provide the software as a stand-alone offering for others to use.
5. **EXPORT RESTRICTIONS.** You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users and end use.  For further information on export restrictions, visit (aka.ms/exporting).
6. **SUPPORT SERVICES.** Because this software is “as is,” we may not provide support services for it.
7. **ENTIRE AGREEMENT.** This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.
8. **APPLICABLE LAW.**  If you acquired the software in the United States, Washington law applies to interpretation of and claims for breach of this agreement, and the laws of the state where you live apply to all other claims. If you acquired the software in any other country, its laws apply.
9. **CONSUMER RIGHTS; REGIONAL VARIATIONS.** This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:
    * **Australia.** You have statutory guarantees under the Australian Consumer Law and nothing in this agreement is intended to affect those rights.
    * **Canada.** If you acquired this software in Canada, you may stop receiving updates by turning off the automatic update feature, disconnecting your device from the Internet (if and when you re-connect to the Internet, however, the software will resume checking for and installing updates), or uninstalling the software. The product documentation, if any, may also specify how to turn off updates for your specific device or software.
    * **Germany and Austria.**
        1. **Warranty.** The properly licensed software will perform substantially as described in any Microsoft materials that accompany the software. However, Microsoft gives no contractual guarantee in relation to the licensed software.
        2. **Limitation of Liability.** In case of intentional conduct, gross negligence, claims based on the Product Liability Act, as well as, in case of death or personal or physical injury, Microsoft is liable according to the statutory law.

        Subject to the foregoing clause (2), Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence.
10. **DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED “AS-IS.” YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.**
11. **LIMITATION ON AND EXCLUSION OF DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.**
    
    This limitation applies to (a) anything related to the software, services, content (including code) on third party Internet sites, or third party applications; and (b) claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.
    
    It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your country may not allow the exclusion or limitation of incidental, consequential or other damages.

**Please note: As this software is distributed in Quebec, Canada, some of the clauses in this agreement are provided below in French.**

**Remarque : Ce logiciel étant distribué au Québec, Canada, certaines des clauses dans ce contrat sont fournies ci-dessous en français.**

**EXONÉRATION DE GARANTIE.** Le logiciel visé par une licence est offert « tel quel ». Toute utilisation de ce logiciel est à votre seule risque et péril. Microsoft n’accorde aucune autre garantie expresse. Vous pouvez bénéficier de droits additionnels en vertu du droit local sur la protection des consommateurs, que ce contrat ne peut modifier. La ou elles sont permises par le droit locale, les garanties implicites de qualité marchande, d’adéquation à un usage particulier et d’absence de contrefaçon sont exclues.

**LIMITATION DES DOMMAGES-INTÉRÊTS ET EXCLUSION DE RESPONSABILITÉ POUR LES DOMMAGES.** Vous pouvez obtenir de Microsoft et de ses fournisseurs une indemnisation en cas de dommages directs uniquement à hauteur de 5,00 $ US. Vous ne pouvez prétendre à aucune indemnisation pour les autres dommages, y compris les dommages spéciaux, indirects ou accessoires et pertes de bénéfices.

Cette limitation concerne:

* tout ce qui est relié au logiciel, aux services ou au contenu (y compris le code) figurant sur des sites Internet tiers ou dans des programmes tiers ; et
* les réclamations au titre de violation de contrat ou de garantie, ou au titre de responsabilité stricte, de négligence ou d’une autre faute dans la limite autorisée par la loi en vigueur.

Elle s’applique également, même si Microsoft connaissait ou devrait connaître l’éventualité d’un tel dommage. Si votre pays n’autorise pas l’exclusion ou la limitation de responsabilité pour les dommages indirects, accessoires ou de quelque nature que ce soit, il se peut que la limitation ou l’exclusion ci-dessus ne s’appliquera pas à votre égard.

**EFFET JURIDIQUE.** Le présent contrat décrit certains droits juridiques. Vous pourriez avoir d’autres droits prévus par les lois de votre pays. Le présent contrat ne modifie pas les droits que vous confèrent les lois de votre pays si celles-ci ne le permettent pas.
```
<!-- generated by scripts/notices/notices.py: end -->
