# art —— 热更散图

会随补丁换内容的图（卡面、头像、关卡插图），从 arts/ 挑进来，按文件名加载
（如 `卡001_炎爆术.png`）。运行时由 `Loader/ArtStore` 懒加载，查找顺序 Mods/art → Config/art。
