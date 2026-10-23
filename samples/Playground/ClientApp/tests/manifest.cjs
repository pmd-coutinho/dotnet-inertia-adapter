const path = require('node:path')
const { spawnSync } = require('node:child_process')
const root = path.resolve(__dirname, '..')
function run(arguments, env = process.env) {
  const result = spawnSync('dotnet', arguments, { cwd: root, env, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}
const manifest = path.resolve(root, '../endpoints.json')
run(['run', '--project', '../Playground.csproj', '--no-launch-profile'], { ...process.env, PATHFINDER_EXPORT_ENDPOINTS: manifest })
const args = ['run', '--project', '../../../src/InertiaNet.Pathfinder', '--framework', 'net10.0', '--',
  '--project', '../Playground.csproj', '--output', 'src/pathfinder', '--contracts', '../contracts.json', '--endpoint-manifest', manifest, '--strict', '--quiet']
run(args)
run([...args, '--check'])
