// Exercise packaged tools and buildTransitive targets, rather than dotnet run.
const fs = require('node:fs')
const os = require('node:os')
const path = require('node:path')
const assert = require('node:assert/strict')
const { spawnSync } = require('node:child_process')
const [feed, version, ...frameworks] = process.argv.slice(2)
if (!feed || !version || !frameworks.length) throw new Error('Usage: node scripts/test-pathfinder-package.cjs <feed> <version> <tfm...>')
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'pathfinder-package-'))
function run(cwd, ...args) {
  return execute(cwd, 'dotnet', args)
}
function execute(cwd, command, args) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', timeout: 120000,
    env: { ...process.env, NUGET_PACKAGES: path.join(root, 'packages'), DOTNET_CLI_HOME: path.join(root, 'cli-home'), DOTNET_NOLOGO: '1' } })
  if (result.error) throw result.error
  assert.equal(result.status, 0, `${args.join(' ')}\n${result.stdout}\n${result.stderr}`)
  return result.stdout
}
try {
  for (const framework of frameworks) {
    const project = path.join(root, framework)
    fs.mkdirSync(project)
    // Only this freshly packed feed can provide the tool/build packages.
    fs.writeFileSync(path.join(project, 'NuGet.Config'), `<configuration><packageSources><clear/><add key="packed" value="${path.resolve(feed).replaceAll('&', '&amp;').replaceAll('"', '&quot;')}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="packed"><package pattern="InertiaNet.*"/></packageSource><packageSource key="nuget"><package pattern="Microsoft.*"/></packageSource></packageSourceMapping></configuration>`)
    const toolPath = path.join(project, 'tools')
    run(project, 'tool', 'install', 'InertiaNet.Pathfinder', '--version', version, '--framework', framework, '--tool-path', toolPath, '--no-cache')
    const tool = path.join(toolPath, process.platform === 'win32' ? 'pathfinder.exe' : 'pathfinder')
    assert.match(execute(project, tool, ['--version']), /^\d+\.\d+\.\d+/)
    fs.writeFileSync(path.join(project, 'App.csproj'), `<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>${framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><PathfinderEnabled>true</PathfinderEnabled><PathfinderQuiet>false</PathfinderQuiet><PathfinderToolCommand>&quot;${tool}&quot;</PathfinderToolCommand></PropertyGroup><ItemGroup><PackageReference Include="InertiaNet.Pathfinder.Build" Version="${version}" PrivateAssets="all" /></ItemGroup></Project>`)
    fs.writeFileSync(path.join(project, 'Program.cs'), 'var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\napp.MapGet("/items/{id:int}", (int id) => id).WithName("items.show");\napp.Run();\n')
    assert.match(run(project, 'build', 'App.csproj', '--nologo'), /Running Pathfinder/)
    const output = path.join(project, 'ClientApp/pathfinder')
    const module = path.join(output, 'routes/items/index.ts')
    assert.match(fs.readFileSync(module, 'utf8'), /export const show/)
    const modified = fs.statSync(module).mtimeMs
    assert.doesNotMatch(run(project, 'build', 'App.csproj', '--no-restore', '--nologo'), /Running Pathfinder/)
    assert.equal(fs.statSync(module).mtimeMs, modified)
    execute(project, tool, ['--project', 'App.csproj', '--output', output, '--check', '--quiet'])
    fs.unlinkSync(module)
    assert.match(run(project, 'build', 'App.csproj', '--no-restore', '--nologo'), /Running Pathfinder/)
    assert.ok(fs.existsSync(module))
    console.log(`${framework}: installed CLI generation/check and packaged build/no-op/repair passed`)
    // Also exercise the default local-tool command supported by the companion.
    run(project, 'new', 'tool-manifest')
    run(project, 'tool', 'install', 'InertiaNet.Pathfinder', '--version', version, '--no-cache')
    run(project, 'tool', 'restore')
    assert.match(run(project, 'tool', 'run', 'pathfinder', '--version'), /^\d+\.\d+\.\d+/)
    assert.match(run(project, 'build', 'App.csproj', '--no-restore', '--nologo', '-property:PathfinderToolCommand=dotnet tool run pathfinder'), /Running Pathfinder/)
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true })
}
