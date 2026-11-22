package memhost.paint;

import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.world.WorldLoadEvent;
import org.bukkit.plugin.java.JavaPlugin;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

/**
 * Строит мир из рисунка MemHost: файл plugins/MemHostPaint/paint.txt.
 * Строк — координата Z, материалов в строке — координата X.
 */
public final class PaintPlugin extends JavaPlugin implements Listener {

    private static final int BASE_Y = 64;

    private Material[][] art = new Material[0][0];
    private boolean hasArt;

    @Override
    public void onEnable() {
        saveDefaultConfig();
        getServer().getPluginManager().registerEvents(this, this);
        loadArt();
        World world = Bukkit.getWorld(getConfig().getString("world", "world"));
        if (world != null) {
            Bukkit.getScheduler().runTask(this, () -> build(world));
        }
        getLogger().info("MemHost Paint готов: " + (hasArt ? size() + "x" + size() + " рисунок загружен" : "рисунка нет"));
    }

    @EventHandler
    public void onWorldLoad(WorldLoadEvent event) {
        World world = event.getWorld();
        if (!world.getName().equalsIgnoreCase(getConfig().getString("world", "world"))) return;
        Bukkit.getScheduler().runTask(this, () -> build(world));
    }

    @EventHandler
    public void onJoin(PlayerJoinEvent event) {
        Player player = event.getPlayer();
        if (!player.getWorld().getName().equalsIgnoreCase(getConfig().getString("world", "world"))) return;
        if (player.getLocation().getY() < BASE_Y) {
            player.teleport(spawn(player.getWorld()));
        }
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        if (args.length > 0 && args[0].equalsIgnoreCase("build")) {
            loadArt();
            World world = Bukkit.getWorld(getConfig().getString("world", "world"));
            if (world == null) {
                sender.sendMessage("Мир не загружен");
                return true;
            }
            build(world);
            sender.sendMessage(hasArt ? "Рисунок построен" : "Файл paint.txt не найден");
            return true;
        }
        sender.sendMessage("Использование: /memhost build");
        return true;
    }

    private int size() {
        return art.length;
    }

    private void loadArt() {
        hasArt = false;
        art = new Material[0][0];
        Path file = getDataFolder().toPath().resolve("paint.txt");
        if (!Files.isRegularFile(file)) return;
        Material fallback = Material.matchMaterial(getConfig().getString("fallback", "STONE"));
        if (fallback == null) fallback = Material.STONE;
        try {
            List<String> lines = Files.readAllLines(file, StandardCharsets.UTF_8);
            int height = 0;
            int width = 0;
            for (String line : lines) {
                if (line.trim().isEmpty()) continue;
                height++;
                width = Math.max(width, split(line).length);
            }
            if (height == 0 || width == 0) return;
            art = new Material[height][width];
            int z = 0;
            for (String line : lines) {
                if (line.trim().isEmpty()) continue;
                String[] cells = split(line);
                for (int x = 0; x < width; x++) {
                    String name = x < cells.length ? cells[x].trim().toUpperCase() : "";
                    if (name.isEmpty() || name.equals("AIR")) {
                        art[z][x] = Material.AIR;
                        continue;
                    }
                    Material material = Material.matchMaterial(name);
                    art[z][x] = material != null && material.isBlock() ? material : fallback;
                }
                z++;
            }
            hasArt = true;
        } catch (IOException e) {
            getLogger().warning("Не удалось прочитать paint.txt: " + e.getMessage());
        }
    }

    private static String[] split(String line) {
        return line.trim().split("[,;\\s]+");
    }

    private void build(World world) {
        int size = size();
        if (size == 0) {
            world.getBlockAt(0, BASE_Y, 0).setType(Material.STONE, false);
            getLogger().warning("Рисунок пустой — поставил один камень, чтобы было где стоять");
            return;
        }
        int width = art[0].length;
        boolean anySolid = false;
        for (int z = 0; z < size; z++) {
            for (int x = 0; x < width; x++) {
                Material material = art[z][x] == null ? Material.AIR : art[z][x];
                anySolid |= material.isSolid();
                world.getBlockAt(x, BASE_Y, z).setType(material, false);
                for (int y = BASE_Y + 1; y <= BASE_Y + 4; y++) {
                    if (!world.getBlockAt(x, y, z).isEmpty()) {
                        world.getBlockAt(x, y, z).setType(Material.AIR, false);
                    }
                }
            }
        }
        if (!anySolid) {
            world.getBlockAt(width / 2, BASE_Y, size / 2).setType(Material.STONE, false);
            getLogger().warning("Рисунок пустой — поставил один камень в центр, чтобы было где стоять");
        }
        world.getWorldBorder().setCenter(width / 2.0, size / 2.0);
        world.getWorldBorder().setSize(Math.max(width, size));
        Location spawn = spawn(world);
        world.setSpawnLocation(spawn);
        for (Player player : world.getPlayers()) {
            player.teleport(spawn);
        }
        getLogger().info("Мир " + width + "x" + size + " построен в " + world.getName());
    }

    private Location spawn(World world) {
        int size = size();
        int width = size == 0 ? 0 : art[0].length;
        for (int z = 0; z < size; z++) {
            for (int x = 0; x < width; x++) {
                Material material = art[z][x];
                if (material != null && material != Material.AIR) {
                    return new Location(world, x + 0.5, BASE_Y + 1, z + 0.5);
                }
            }
        }
        return new Location(world, width / 2.0 + 0.5, BASE_Y + 1, size / 2.0 + 0.5);
    }
}
