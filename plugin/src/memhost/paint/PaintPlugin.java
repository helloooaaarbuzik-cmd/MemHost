package memhost.paint;

import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.block.Block;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.block.Action;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.event.player.PlayerInteractEvent;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.world.WorldLoadEvent;
import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.InventoryHolder;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.plugin.java.JavaPlugin;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Random;

/**
 * Строит мир MemHost по режиму из config.yml:
 * paint — из рисунка paint.txt, oneblock — один блок, skyblock — остров,
 * objects — панель объектов в игре, standart — ничего.
 */
public final class PaintPlugin extends JavaPlugin implements Listener {

    private static final int BASE_Y = 64;
    private static final String PANEL_TITLE = "Объекты MemPaint";
    private static final Material[] ONEBLOCK_POOL = {
            Material.STONE, Material.COBBLESTONE, Material.DIRT, Material.OAK_LOG,
            Material.COAL_ORE, Material.IRON_ORE, Material.SAND, Material.GRAVEL,
            Material.OAK_PLANKS, Material.GLASS, Material.GOLD_ORE, Material.DIAMOND_ORE
    };

    private final Random random = new Random();
    private Material[][] art = new Material[0][0];
    private boolean hasArt;
    private String mode = "paint";

    @Override
    public void onEnable() {
        saveDefaultConfig();
        mode = getConfig().getString("mode", "paint").toLowerCase();
        getServer().getPluginManager().registerEvents(this, this);
        loadArt();
        World world = Bukkit.getWorld(getConfig().getString("world", "world"));
        if (world != null) {
            Bukkit.getScheduler().runTask(this, () -> build(world));
        }
        getLogger().info("MemHost готов, режим: " + mode);
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
        if (mode.equals("standart")) return;
        if (player.getLocation().getY() < BASE_Y) {
            player.teleport(spawn(player.getWorld()));
        }
        if (mode.equals("objects")) {
            givePanel(player);
        }
    }

    @EventHandler
    public void onBreak(BlockBreakEvent event) {
        if (!mode.equals("oneblock")) return;
        Block block = event.getBlock();
        if (block.getX() != 0 || block.getY() != BASE_Y || block.getZ() != 0) return;
        Bukkit.getScheduler().runTask(this, () -> block.setType(ONEBLOCK_POOL[random.nextInt(ONEBLOCK_POOL.length)], false));
    }

    @EventHandler
    public void onInteract(PlayerInteractEvent event) {
        if (!mode.equals("objects")) return;
        if (event.getAction() != Action.RIGHT_CLICK_AIR && event.getAction() != Action.RIGHT_CLICK_BLOCK) return;
        ItemStack item = event.getItem();
        if (item == null || item.getType() != Material.BLAZE_ROD) return;
        event.setCancelled(true);
        event.getPlayer().openInventory(createMenu());
    }

    @EventHandler
    public void onMenuClick(InventoryClickEvent event) {
        if (!(event.getInventory().getHolder() instanceof ObjectMenu)) return;
        event.setCancelled(true);
        if (!(event.getWhoClicked() instanceof Player player)) return;
        ItemStack clicked = event.getCurrentItem();
        if (clicked == null || clicked.getType() == Material.AIR) return;
        String name = clicked.getType().name();
        player.closeInventory();
        spawnObject(player, name);
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
            sender.sendMessage("Режим " + mode + " построен");
            return true;
        }
        if (args.length > 0 && args[0].equalsIgnoreCase("objects")) {
            if (!(sender instanceof Player player)) {
                sender.sendMessage("Панель доступна только в игре");
                return true;
            }
            player.openInventory(createMenu());
            return true;
        }
        sender.sendMessage("Использование: /memhost build | /memhost objects");
        return true;
    }

    private void givePanel(Player player) {
        for (ItemStack stack : player.getInventory().getContents()) {
            if (stack != null && stack.getType() == Material.BLAZE_ROD) return;
        }
        ItemStack item = new ItemStack(Material.BLAZE_ROD);
        ItemMeta meta = item.getItemMeta();
        if (meta != null) {
            meta.displayName(Component.text("Панель объектов", NamedTextColor.GOLD));
            meta.lore(List.of(Component.text("ПКМ — открыть список", NamedTextColor.GRAY)));
            item.setItemMeta(meta);
        }
        player.getInventory().addItem(item);
    }

    private Inventory createMenu() {
        ObjectMenu holder = new ObjectMenu();
        var inventory = Bukkit.createInventory(holder, 27, Component.text(PANEL_TITLE));
        holder.set(inventory);
        addIcon(inventory, 10, Material.BRICKS, "Дом");
        addIcon(inventory, 11, Material.OAK_SAPLING, "Дерево");
        addIcon(inventory, 12, Material.CHEST, "Сундук");
        addIcon(inventory, 13, Material.STONE_BRICKS, "Башня");
        addIcon(inventory, 14, Material.WATER_BUCKET, "Пруд");
        addIcon(inventory, 15, Material.GLOWSTONE, "Фонарь");
        return inventory;
    }

    private void addIcon(Inventory inventory, int slot, Material material, String name) {
        ItemStack item = new ItemStack(material);
        ItemMeta meta = item.getItemMeta();
        if (meta != null) {
            meta.displayName(Component.text(name, NamedTextColor.WHITE));
            item.setItemMeta(meta);
        }
        inventory.setItem(slot, item);
    }

    private void spawnObject(Player player, String materialName) {
        Location target = player.getLocation().add(player.getLocation().getDirection().setY(0).normalize().multiply(3));
        int x = target.getBlockX();
        int y = target.getBlockY();
        int z = target.getBlockZ();
        World world = player.getWorld();
        switch (materialName) {
            case "BRICKS" -> house(world, x, y, z);
            case "OAK_SAPLING" -> tree(world, x, y, z);
            case "CHEST" -> world.getBlockAt(x, y, z).setType(Material.CHEST, false);
            case "STONE_BRICKS" -> tower(world, x, y, z);
            case "WATER_BUCKET" -> pond(world, x, y, z);
            case "GLOWSTONE" -> lamp(world, x, y, z);
            default -> {
                return;
            }
        }
        player.sendMessage("Объект поставлен");
    }

    private void house(World world, int x, int y, int z) {
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                for (int dy = 0; dy <= 2; dy++) {
                    boolean wall = Math.abs(dx) == 2 || Math.abs(dz) == 2;
                    Material material = wall ? Material.OAK_PLANKS : Material.AIR;
                    if (wall && dy == 2) material = Material.OAK_LOG;
                    world.getBlockAt(x + dx, y + dy, z + dz).setType(material, false);
                }
                world.getBlockAt(x + dx, y + 3, z + dz).setType(Material.OAK_PLANKS, false);
                world.getBlockAt(x + dx, y, z + dz).setType(Material.OAK_PLANKS, false);
            }
        }
        world.getBlockAt(x, y, z + 2).setType(Material.AIR, false);
        world.getBlockAt(x, y + 1, z + 2).setType(Material.AIR, false);
    }

    private void tree(World world, int x, int y, int z) {
        for (int dy = 0; dy < 4; dy++) {
            world.getBlockAt(x, y + dy, z).setType(Material.OAK_LOG, false);
        }
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                for (int dy = 3; dy <= 5; dy++) {
                    int distance = Math.abs(dx) + Math.abs(dz) + Math.abs(dy - 4);
                    if (distance <= 3) world.getBlockAt(x + dx, y + dy, z + dz).setType(Material.OAK_LEAVES, false);
                }
            }
        }
    }

    private void tower(World world, int x, int y, int z) {
        for (int dy = 0; dy < 8; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    boolean edge = Math.abs(dx) == 1 || Math.abs(dz) == 1;
                    if (!edge) continue;
                    Material material = dy == 0 || dy == 4 ? Material.STONE_BRICKS : Material.MOSSY_STONE_BRICKS;
                    world.getBlockAt(x + dx, y + dy, z + dz).setType(material, false);
                }
            }
            world.getBlockAt(x, y + dy, z).setType(Material.AIR, false);
        }
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                world.getBlockAt(x + dx, y + 8, z + dz).setType(Material.STONE_BRICKS, false);
            }
        }
        world.getBlockAt(x, y + 8, z).setType(Material.GLOWSTONE, false);
    }

    private void pond(World world, int x, int y, int z) {
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                world.getBlockAt(x + dx, y - 1, z + dz).setType(Material.SAND, false);
                world.getBlockAt(x + dx, y, z + dz).setType(Material.WATER, false);
            }
        }
    }

    private void lamp(World world, int x, int y, int z) {
        for (int dy = 0; dy < 3; dy++) {
            world.getBlockAt(x, y + dy, z).setType(Material.OAK_FENCE, false);
        }
        world.getBlockAt(x, y + 3, z).setType(Material.GLOWSTONE, false);
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
        switch (mode) {
            case "oneblock" -> buildOneBlock(world);
            case "skyblock" -> buildSkyBlock(world);
            case "objects" -> buildObjects(world);
            case "standart" -> getLogger().info("Режим standart: мир не меняю");
            default -> buildPaint(world);
        }
    }

    private void buildPaint(World world) {
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

    private void buildOneBlock(World world) {
        world.getBlockAt(0, BASE_Y, 0).setType(Material.STONE, false);
        for (int y = BASE_Y + 1; y <= BASE_Y + 4; y++) {
            world.getBlockAt(0, y, 0).setType(Material.AIR, false);
        }
        world.getWorldBorder().setCenter(0.5, 0.5);
        world.getWorldBorder().setSize(16);
        Location spawn = new Location(world, 0.5, BASE_Y + 1, 0.5);
        world.setSpawnLocation(spawn);
        getLogger().info("Режим OneBlock: один блок на 0, " + BASE_Y + ", 0");
    }

    private void buildSkyBlock(World world) {
        for (int x = -2; x <= 2; x++) {
            for (int z = -2; z <= 2; z++) {
                world.getBlockAt(x, BASE_Y - 1, z).setType(Material.GRASS_BLOCK, false);
                world.getBlockAt(x, BASE_Y - 2, z).setType(Material.DIRT, false);
                world.getBlockAt(x, BASE_Y - 3, z).setType(Material.DIRT, false);
                for (int y = BASE_Y; y <= BASE_Y + 8; y++) {
                    if (!world.getBlockAt(x, y, z).isEmpty()) {
                        world.getBlockAt(x, y, z).setType(Material.AIR, false);
                    }
                }
            }
        }
        for (int y = BASE_Y; y < BASE_Y + 4; y++) {
            world.getBlockAt(0, y, 0).setType(Material.OAK_LOG, false);
        }
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                for (int dy = 3; dy <= 5; dy++) {
                    int distance = Math.abs(dx) + Math.abs(dz) + Math.abs(dy - 4);
                    if (distance <= 3) world.getBlockAt(dx, BASE_Y + dy, dz).setType(Material.OAK_LEAVES, false);
                }
            }
        }
        world.getWorldBorder().setCenter(0.5, 0.5);
        world.getWorldBorder().setSize(64);
        Location spawn = new Location(world, 0.5, BASE_Y, 0.5);
        world.setSpawnLocation(spawn);
        getLogger().info("Режим SkyBlock: остров 5x5 с деревом");
    }

    private void buildObjects(World world) {
        for (int x = -4; x <= 4; x++) {
            for (int z = -4; z <= 4; z++) {
                world.getBlockAt(x, BASE_Y - 1, z).setType(Material.GRASS_BLOCK, false);
                world.getBlockAt(x, BASE_Y - 2, z).setType(Material.DIRT, false);
                for (int y = BASE_Y; y <= BASE_Y + 6; y++) {
                    if (!world.getBlockAt(x, y, z).isEmpty()) {
                        world.getBlockAt(x, y, z).setType(Material.AIR, false);
                    }
                }
            }
        }
        world.getWorldBorder().setCenter(0.5, 0.5);
        world.getWorldBorder().setSize(128);
        Location spawn = new Location(world, 0.5, BASE_Y, 0.5);
        world.setSpawnLocation(spawn);
        getLogger().info("Режим WHOOOOOOO: площадка 9x9 и панель объектов");
    }

    private Location spawn(World world) {
        if (mode.equals("oneblock")) {
            return new Location(world, 0.5, BASE_Y + 1, 0.5);
        }
        if (mode.equals("skyblock") || mode.equals("objects")) {
            return new Location(world, 0.5, BASE_Y, 0.5);
        }
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

    public static final class ObjectMenu implements InventoryHolder {
        private Inventory inventory;

        void set(Inventory inventory) {
            this.inventory = inventory;
        }

        @Override
        public Inventory getInventory() {
            return inventory;
        }
    }
}
