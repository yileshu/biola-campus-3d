--[[
	Client: day/night toggle (N), street lamps and lit windows at night, controls hint and the
	data attribution that OpenStreetMap's licence requires.
]]
local Players = game:GetService("Players")
local Lighting = game:GetService("Lighting")
local UserInputService = game:GetService("UserInputService")

local player = Players.LocalPlayer
local campus = workspace:WaitForChild("Campus")

-- ---------------------------------------------------------------- HUD
local gui = Instance.new("ScreenGui")
gui.Name = "BiolaHud"
gui.ResetOnSpawn = false
gui.IgnoreGuiInset = true

local function label(text, anchor, pos, size, textSize)
	local t = Instance.new("TextLabel")
	t.BackgroundTransparency = 0.45
	t.BackgroundColor3 = Color3.new(0, 0, 0)
	t.TextColor3 = Color3.new(1, 1, 1)
	t.Font = Enum.Font.Gotham
	t.TextSize = textSize
	t.Text = text
	t.AnchorPoint = anchor
	t.Position = pos
	t.Size = size
	t.Parent = gui
	return t
end

label("Map data © OpenStreetMap contributors (ODbL) · Terrain & heights: USGS 3DEP lidar · Unofficial fan project",
	Vector2.new(1, 1), UDim2.new(1, -8, 1, -8), UDim2.fromOffset(640, 22), 13)
local hint = label("Biola University · press N for day / night", Vector2.new(0, 0), UDim2.fromOffset(12, 60), UDim2.fromOffset(300, 26), 15)
gui.Parent = player:WaitForChild("PlayerGui")
task.delay(12, function()
	hint.Visible = false
end)

-- ---------------------------------------------------------------- day / night
local DAY_TIME, NIGHT_TIME = 15.5, 20.75
local night = false
local lampLights, lampHeads, windows = {}, {}, {}

local function collect()
	lampLights, lampHeads, windows = {}, {}, {}
	for _, d in ipairs(campus:GetDescendants()) do
		if d:IsA("PointLight") and d.Name == "LampLight" then
			table.insert(lampLights, d)
		elseif d:IsA("BasePart") then
			if d.Name == "LampHead" then
				table.insert(lampHeads, d)
			elseif d.Name == "Window" then
				table.insert(windows, { part = d, color = d.Color })
			end
		end
	end
end

local WARM = Color3.fromRGB(255, 205, 140)

local function apply()
	Lighting.ClockTime = night and NIGHT_TIME or DAY_TIME
	for _, l in ipairs(lampLights) do
		l.Enabled = night
	end
	for _, h in ipairs(lampHeads) do
		h.Material = night and Enum.Material.Neon or Enum.Material.Glass
		h.Color = night and WARM or Color3.fromRGB(239, 230, 208)
	end
	for i, w in ipairs(windows) do
		-- about 40% of windows lit at night, chosen deterministically
		local lit = night and ((i * 2654435761) % 100) < 40
		w.part.Material = lit and Enum.Material.Neon or Enum.Material.Glass
		w.part.Color = lit and WARM or w.color
	end
end

task.defer(function()
	collect()
	apply()
end)

UserInputService.InputBegan:Connect(function(input, processed)
	if processed then return end
	if input.KeyCode == Enum.KeyCode.N then
		night = not night
		if #windows == 0 then collect() end
		apply()
	end
end)
