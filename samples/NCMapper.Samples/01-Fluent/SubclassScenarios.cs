using System.Linq;
using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 场景 107 — 多态子类型映射 MapSubclass（how-to/fluent/terminals.md「MapSubclass」节）：
/// Fluent 对级登记 + 特性侧 [MapSubclass] 多实例；isinst 分派链（Map/MapList/_Update 双实例）
/// + 最派生优先 + 未登记派生回落基类兜底体 + 反向子类对自动产出（登记非单向）。
/// </summary>
public static class SubclassScenarios
{
    /// <summary>
    /// Fluent 侧基类对映射——运行时按源实际类型路由子类分派体。
    /// </summary>
    /// <param name="src">动物实体（可能是任一派生类型）</param>
    /// <returns>基类静态类型的 DTO（运行时实际为对应子类 DTO）</returns>
    public static ScAnimalDto FluentSide(ScAnimalEntity src)
        => Mapper.Map<ScAnimalEntity, ScAnimalDto>(src)!;

    /// <summary>
    /// Fluent 侧 _Update 形态——双实例匹配（src 与 dst 同时 isinst 命中才路由子类体）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="existing">既有 DTO（运行时类型决定分派）</param>
    public static void FluentUpdate(ScAnimalEntity src, ScAnimalDto existing)
        => Mapper.Map<ScAnimalEntity, ScAnimalDto>(src, existing);

    /// <summary>
    /// 特性侧映射——类级 [MapSubclass] 多实例登记的分派。
    /// </summary>
    /// <param name="src">动物实体</param>
    /// <returns>特性侧基类 DTO（运行时对应子类）</returns>
    public static ScAttrAnimalDto AttributeSide(ScAnimalEntity src)
        => Mapper.Map<ScAnimalEntity, ScAttrAnimalDto>(src)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部子场景并输出。
    /// </summary>
    public static void Run()
    {
        #region MapSubclass_Dispatch
        // 1. Fluent Map dispatch: Dog -> DogDto (subclass body, dedicated fields filled)
        var dog = new ScDogEntity { Name = "Rex", Weight = 12.5, Breed = "Labrador" };
        var dogDto = FluentSide(dog);
        OutputHelper.Dump("107-subclass-map-dog", dog, dogDto);
        var asDog = dogDto as ScDogDto;
        if (asDog is null)
            OutputHelper.Error($"107-subclass-map-dog: DogEntity should dispatch to DogDto (actual: {dogDto.GetType().Name})");
        else if (asDog.Breed != "Labrador" || asDog.Name != "Rex")
            OutputHelper.Error($"107-subclass-map-dog: subclass body should map dedicated fields (Breed={asDog.Breed}, Name={asDog.Name})");

        // 2. Fluent Map dispatch: Cat -> CatDto
        var cat = new ScCatEntity { Name = "Mia", Weight = 4.2, Indoor = true };
        var catDto = FluentSide(cat);
        OutputHelper.Dump("107-subclass-map-cat", cat, catDto);
        var asCat = catDto as ScCatDto;
        if (asCat is null)
            OutputHelper.Error($"107-subclass-map-cat: CatEntity should dispatch to CatDto (actual: {catDto.GetType().Name})");
        else if (!asCat.Indoor)
            OutputHelper.Error($"107-subclass-map-cat: subclass field Indoor should be true");

        // 3. Unregistered derived type falls back to the base catch-all body: Bird goes through the AnimalDto base body (subclass-dedicated fields have no source)
        var bird = new ScBirdEntity { Name = "Kiwi", Weight = 0.9, Wingspan = 0.4 };
        var birdDto = FluentSide(bird);
        OutputHelper.Dump("107-subclass-fallback-bird", bird, birdDto);
        if (birdDto.GetType() != typeof(ScAnimalDto))
            OutputHelper.Error($"107-subclass-fallback-bird: unregistered derived type should fall back to base body AnimalDto (actual: {birdDto.GetType().Name})");
        if (birdDto.Name != "Kiwi" || birdDto.Weight != 0.9)
            OutputHelper.Error($"107-subclass-fallback-bird: fallback body should map base fields as usual (Name={birdDto.Name}, Weight={birdDto.Weight})");

        // 4. MapList mixed list: per-element dispatch by runtime type + fallback
        var zoo = new List<ScAnimalEntity> { dog, cat, bird };
        var zooDtos = Mapper.MapList<ScAnimalEntity, ScAnimalDto>(zoo);
        OutputHelper.Dump("107-subclass-maplist", zoo, zooDtos);
        if (zooDtos is null || zooDtos.Count != 3)
            OutputHelper.Error("107-subclass-maplist: 3 elements should produce 3 DTOs");
        else if (zooDtos[0] is not ScDogDto || zooDtos[1] is not ScCatDto || zooDtos[2] is not ScAnimalDto)
            OutputHelper.Error($"107-subclass-maplist: elements should dispatch one by one (actual: {zooDtos[0].GetType().Name}/{zooDtos[1].GetType().Name}/{zooDtos[2].GetType().Name})");

        // 5. _Update dual-instance match: dst runtime type DogDto -- route to subclass body; dst runtime type base class -- fall back to base body (F50 dual-instance-fallback)
        var dogExisting = new ScDogDto { Name = "old", Weight = 0, Breed = "keep-breed" };
        FluentUpdate(dog, dogExisting);
        OutputHelper.Dump("107-subclass-update-dispatch", dog, dogExisting);
        if (dogExisting.Breed != "Labrador" || dogExisting.Name != "Rex")
            OutputHelper.Error($"107-subclass-update-dispatch: dual-instance match should route to subclass body (Name={dogExisting.Name}, Breed={dogExisting.Breed})");

        var baseExisting = new ScAnimalDto { Name = "old", Weight = 0 };
        FluentUpdate(dog, baseExisting);
        OutputHelper.Dump("107-subclass-update-fallback", dog, baseExisting);
        if (baseExisting.Name != "Rex" || baseExisting.Weight != 12.5)
            OutputHelper.Error($"107-subclass-update-fallback: non-subclass dst should fall back to base body and update common fields (Name={baseExisting.Name}, Weight={baseExisting.Weight})");

        // 6. Reverse subclass pair (registration is not one-way; the reverse registration arm is produced automatically): DogDto -> DogEntity
        var reverseSrc = new ScDogDto { Name = "Back", Weight = 9.9, Breed = "Corgi" };
        var reverseEntity = Mapper.Map<ScDogDto, ScDogEntity>(reverseSrc);
        OutputHelper.Dump("107-subclass-reverse", reverseSrc, reverseEntity);
        if (reverseEntity is null || reverseEntity.Breed != "Corgi" || reverseEntity.Name != "Back")
            OutputHelper.Error($"107-subclass-reverse: reverse subclass pair should be auto-generated and map dedicated fields (Breed={reverseEntity?.Breed}, Name={reverseEntity?.Name})");
        #endregion

        // 7. 特性侧 [MapSubclass]：类级多实例登记，分派语义与 Fluent 等价
        var attrDog = AttributeSide(dog);
        OutputHelper.Dump("107-subclass-attr-dog", dog, attrDog);
        var attrAsDog = attrDog as ScAttrDogDto;
        if (attrAsDog is null)
            OutputHelper.Error($"107-subclass-attr-dog: attribute-side registration should dispatch too (actual: {attrDog.GetType().Name})");
        else if (attrAsDog.Breed != "Labrador")
            OutputHelper.Error($"107-subclass-attr-dog: attribute-side subclass body should fill dedicated fields (Breed={attrAsDog.Breed})");

        var attrCat = AttributeSide(cat);
        OutputHelper.Dump("107-subclass-attr-cat", cat, attrCat);
        if (attrCat is not ScAttrCatDto || ((ScAttrCatDto)attrCat).Indoor != true)
            OutputHelper.Error($"107-subclass-attr-cat: attribute-side Cat registration dispatch failed (actual: {attrCat.GetType().Name})");
    }
}
