-- Builds the lidar/OSM terrain when the place starts, unless it was already baked in Studio
-- (require(game.ServerStorage.BiolaTerrain).Build() in the command bar, then save).
local ServerStorage = game:GetService("ServerStorage")

local terrain = workspace.Terrain
if terrain:GetAttribute("BiolaBuilt") then
	return
end

local spawnPart = workspace:FindFirstChild("Campus") and workspace.Campus:FindFirstChild("World", true)
local first = spawnPart and spawnPart:FindFirstChild("Spawn")
local Builder = require(ServerStorage:WaitForChild("BiolaTerrain"))
Builder.Build({
	firstX = first and first.Position.X or 0,
	firstZ = first and first.Position.Z or 0,
})
