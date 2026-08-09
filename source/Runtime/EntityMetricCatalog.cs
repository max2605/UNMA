using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Trains;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.TreeHarvesters;
using Mafi.Core.Vehicles.TreePlanters;
using Mafi.Core.Vehicles.Trucks;
using UNMA.Api;
using UNMA.Localization;

namespace UNMA.Runtime;

public sealed class MetricDescriptor
{
    public string Path { get; }
    public string Label { get; }
    public double CurrentValue { get; }
    public string Unit { get; }
    public string SuggestedPercentReferencePath { get; }

    public MetricDescriptor(
        string path,
        string label,
        double currentValue,
        string unit = "",
        string suggestedPercentReferencePath = "")
    {
        Path = path;
        Label = label;
        CurrentValue = currentValue;
        Unit = unit ?? "";
        SuggestedPercentReferencePath =
            suggestedPercentReferencePath ?? "";
    }
}

public sealed class EntityInspectionSnapshot
{
    public int EntityId { get; }
    public string Title { get; }
    public string EntityType { get; }
    public string PrototypeId { get; }
    public string StoredProductId { get; }
    public IReadOnlyList<MetricDescriptor> Metrics { get; }
    public string Error { get; }

    public EntityInspectionSnapshot(
        int entityId,
        string title,
        string entityType,
        string prototypeId,
        string storedProductId,
        IReadOnlyList<MetricDescriptor> metrics,
        string error = "")
    {
        EntityId = entityId;
        Title = title ?? "";
        EntityType = entityType ?? "";
        PrototypeId = prototypeId ?? "";
        StoredProductId = storedProductId ?? "";
        Metrics = metrics ?? Array.Empty<MetricDescriptor>();
        Error = error ?? "";
    }
}

public static class EntityMetricCatalog
{
    private const string StoredQuantityPath = "$stored.quantity";
    private const string StorageCapacityPath = "$stored.capacity";
    private const string FillPercentPath = "$stored.percent";
    private const string InputProductPrefix = "$input.product:";
    private const string InputCapacityPrefix = "$input.capacity:";
    private const string TransportQuantityPath = "$transport.quantity";
    private const string TransportCapacityPath = "$transport.capacity";
    private const string TransportFillPercentPath = "$transport.percent";
    private const string TransportProductPrefix = "$transport.product:";
    private const string CargoQuantityPath = "$cargo.quantity";
    private const string CargoCapacityPath = "$cargo.capacity";
    private const string CargoFillPercentPath = "$cargo.percent";
    private const string CargoProductPrefix = "$cargo.product:";

    private static readonly Dictionary<string, ProductProto> s_productsById =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<ushort, ProductProto>
        s_productsBySlimId = new();

    private sealed class ProductBufferTotals
    {
        public ProductProto Product;
        public double Quantity;
        public double Capacity;
    }

    private static readonly string[] s_nestedPropertyHints =
    {
        "Buffer",
        "Products",
        "Maintenance",
        "Electricity",
        "Fuel",
        "Health",
        "Workers",
        "Productivity",
        "Transported",
        "DrivingData",
    };

    public static void ConfigureProducts(ProtosDb protosDb)
    {
        s_productsById.Clear();
        s_productsBySlimId.Clear();

        if (protosDb == null)
        {
            return;
        }

        foreach (var product in protosDb.All<ProductProto>())
        {
            if (product == null || product.SlimId.IsPhantom)
            {
                continue;
            }

            s_productsById[product.Id.Value] = product;
            s_productsBySlimId[product.SlimId.Value] = product;
        }
    }

    public static IReadOnlyList<MetricDescriptor> Discover(IEntity entity)
    {
        var result = new List<MetricDescriptor>();
        var paths = new HashSet<string>(StringComparer.Ordinal);

        Add(result, paths, "$entity.enabled", UnmaText.Get("auto.1c17e8c09407"),
            entity.IsEnabled ? 1d : 0d);
        Add(result, paths, "$entity.paused", UnmaText.Get("auto.d1207434db53"),
            entity.IsPaused ? 1d : 0d);
        Add(result, paths, "$entity.destroyed", UnmaText.Get("auto.cb2b11f3cb70"),
            entity.IsDestroyed ? 1d : 0d);

        if (entity is IEntityWithStoredProductForUi stored)
        {
            var quantity = stored.CurrentQuantity.Value;
            var storedCapacity = stored.Capacity.Value;
            Add(result, paths, StoredQuantityPath,
                UnmaText.Get("metric.stored_quantity"), quantity,
                UnmaText.Get("metric.units"), StorageCapacityPath);
            Add(result, paths, StorageCapacityPath,
                UnmaText.Get("metric.storage_capacity"), storedCapacity,
                UnmaText.Get("metric.units"));
            Add(result, paths, FillPercentPath,
                UnmaText.Get("auto.90817d0fdd55"),
                storedCapacity <= 0
                    ? 0d
                    : quantity * 100d / storedCapacity,
                "%");
        }

        if (entity is IEntityWithInputBuffersForUi inputBuffers)
        {
            AddInputBufferMetrics(result, paths, inputBuffers);
        }

        if (entity is Transport transport)
        {
            AddTransportMetrics(result, paths, transport);
        }

        if (TryGetVehicleCargo(entity, out var vehicleCargo, out var capacity))
        {
            AddVehicleCargoMetrics(
                result,
                paths,
                vehicleCargo,
                capacity);
        }
        else if (TryGetSingleCargo(
                     entity,
                     out var singleCargo,
                     out capacity))
        {
            AddSingleCargoMetrics(result, paths, singleCargo, capacity);
        }

        AddExternalMetrics(result, paths, entity);

        foreach (var property in entity.GetType().GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanRead(property))
            {
                continue;
            }

            object value;
            try
            {
                value = property.GetValue(entity, null);
            }
            catch
            {
                continue;
            }

            if (TryConvertToDouble(value, out var numeric))
            {
                Add(result, paths, property.Name,
                    Humanize(property.Name), numeric);
                continue;
            }

            if (value == null ||
                !s_nestedPropertyHints.Any(
                    hint => property.Name.IndexOf(
                        hint,
                        StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            AddNestedMetrics(
                result,
                paths,
                property.Name,
                value,
                maximum: 24);
        }

        return result
            .OrderBy(MetricSortPriority)
            .ThenBy(metric => metric.Label, StringComparer.CurrentCulture)
            .Take(160)
            .ToArray();
    }

    public static bool TryRead(
        IEntity entity,
        string path,
        out double value)
    {
        value = 0d;
        if (entity == null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("external:", StringComparison.Ordinal))
        {
            var snapshot = UnmaApi.GetSnapshot();
            var metric = snapshot.Metrics.FirstOrDefault(candidate =>
                string.Equals(
                    ExternalPath(candidate.OwnerModId, candidate.Id),
                    path,
                    StringComparison.Ordinal));
            return metric != null && snapshot.TryReadMetric(
                metric.OwnerModId,
                entity.Prototype.Id.Value,
                metric.Id,
                entity,
                out value);
        }

        if (path.StartsWith("$input.", StringComparison.Ordinal))
        {
            return entity is IEntityWithInputBuffersForUi inputBuffers &&
                   TryReadInputBuffers(inputBuffers, path, out value);
        }

        if (path.StartsWith("$transport.", StringComparison.Ordinal))
        {
            return entity is Transport transport &&
                   TryReadTransport(transport, path, out value);
        }

        if (path.StartsWith("$cargo.", StringComparison.Ordinal))
        {
            return TryReadCargo(entity, path, out value);
        }

        switch (path)
        {
            case "$entity.enabled":
                value = entity.IsEnabled ? 1d : 0d;
                return true;
            case "$entity.paused":
                value = entity.IsPaused ? 1d : 0d;
                return true;
            case "$entity.destroyed":
                value = entity.IsDestroyed ? 1d : 0d;
                return true;
            case StoredQuantityPath:
                if (entity is IEntityWithStoredProductForUi storedQuantity)
                {
                    value = storedQuantity.CurrentQuantity.Value;
                    return true;
                }
                return false;
            case StorageCapacityPath:
                if (entity is IEntityWithStoredProductForUi storedCapacity)
                {
                    value = storedCapacity.Capacity.Value;
                    return true;
                }
                return false;
            case FillPercentPath:
                if (entity is IEntityWithStoredProductForUi storedPercent)
                {
                    var capacity = storedPercent.Capacity.Value;
                    value = capacity <= 0
                        ? 0d
                        : storedPercent.CurrentQuantity.Value * 100d /
                          capacity;
                    return true;
                }
                return false;
        }

        object current = entity;
        foreach (var segment in path.Split('.'))
        {
            if (current == null)
            {
                return false;
            }

            var type = current.GetType();
            var property = type.GetProperty(
                segment,
                BindingFlags.Instance | BindingFlags.Public);
            if (property != null && CanRead(property))
            {
                try
                {
                    current = property.GetValue(current, null);
                    continue;
                }
                catch
                {
                    return false;
                }
            }

            var field = type.GetField(
                segment,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                return false;
            }

            try
            {
                current = field.GetValue(current);
            }
            catch
            {
                return false;
            }
        }

        return TryConvertToDouble(current, out value);
    }

    public static string ExternalPath(string ownerModId, string metricId)
    {
        return "external:" + Uri.EscapeDataString(ownerModId ?? "") + ":" +
               Uri.EscapeDataString(metricId ?? "");
    }

    private static void AddExternalMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        IEntity entity)
    {
        var snapshot = UnmaApi.GetSnapshot();
        var prototypeId = entity.Prototype.Id.Value;
        var candidates = snapshot.Metrics
            .Where(metric => string.Equals(
                    metric.PrototypeId,
                    prototypeId,
                    StringComparison.Ordinal) ||
                string.Equals(
                    metric.PrototypeId,
                    "*",
                    StringComparison.Ordinal))
            .OrderBy(metric => string.Equals(
                metric.PrototypeId,
                prototypeId,
                StringComparison.Ordinal) ? 0 : 1)
            .GroupBy(
                metric => ExternalPath(metric.OwnerModId, metric.Id),
                StringComparer.Ordinal)
            .Select(group => group.First())
            .GroupBy(metric => metric.OwnerModId, StringComparer.Ordinal)
            .SelectMany(group => group.Take(16))
            .Take(48);
        foreach (var metric in candidates)
        {
            if (!snapshot.TryReadMetric(
                    metric.OwnerModId,
                    prototypeId,
                    metric.Id,
                    entity,
                    out var value))
            {
                continue;
            }

            var path = ExternalPath(metric.OwnerModId, metric.Id);
            var referencePath = string.IsNullOrWhiteSpace(
                metric.SuggestedReferenceMetric)
                ? ""
                : metric.SuggestedReferenceMetric.StartsWith(
                    "$",
                    StringComparison.Ordinal)
                    ? metric.SuggestedReferenceMetric
                    : ExternalPath(
                        metric.OwnerModId,
                        metric.SuggestedReferenceMetric);
            Add(
                result,
                paths,
                path,
                UnmaText.Resolve(
                    metric.LabelKey,
                    string.IsNullOrWhiteSpace(metric.LabelFallback)
                        ? metric.Id
                        : metric.LabelFallback),
                value,
                metric.Unit,
                referencePath);
        }
    }

    public static string TryGetStoredProductId(IEntity entity)
    {
        if (entity is not IEntityWithStoredProductForUi stored ||
            stored.StoredProduct.IsNone)
        {
            return "";
        }

        return stored.StoredProduct.Value.Id.Value;
    }

    private static void AddInputBufferMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        IEntityWithInputBuffersForUi entity)
    {
        foreach (var totals in CollectInputBufferTotals(entity).Values
                     .OrderBy(item =>
                         GetProductName(item.Product),
                         StringComparer.CurrentCulture))
        {
            var productId = totals.Product.Id.Value;
            var productName = GetProductName(totals.Product);
            Add(
                result,
                paths,
                InputProductPrefix + productId,
                productName + UnmaText.Get("auto.5ef37159ad68"),
                totals.Quantity,
                UnmaText.Get("metric.units"),
                InputCapacityPrefix + productId);
            Add(
                result,
                paths,
                InputCapacityPrefix + productId,
                productName + UnmaText.Get("auto.a98a6884f7ef"),
                totals.Capacity,
                UnmaText.Get("metric.units"));
        }
    }

    private static bool TryReadInputBuffers(
        IEntityWithInputBuffersForUi entity,
        string path,
        out double value)
    {
        value = 0d;
        var quantity = path.StartsWith(
            InputProductPrefix,
            StringComparison.Ordinal);
        var capacity = path.StartsWith(
            InputCapacityPrefix,
            StringComparison.Ordinal);
        if (!quantity && !capacity)
        {
            return false;
        }

        var prefix = quantity ? InputProductPrefix : InputCapacityPrefix;
        var productId = path.Substring(prefix.Length);
        if (string.IsNullOrWhiteSpace(productId))
        {
            return false;
        }

        var found = false;
        foreach (var buffer in entity.InputBuffers ??
                     Enumerable.Empty<IProductBufferReadOnly>())
        {
            if (buffer?.Product == null ||
                !string.Equals(
                    buffer.Product.Id.Value,
                    productId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            found = true;
            value += quantity
                ? buffer.Quantity.Value
                : buffer.Capacity.Value;
        }
        return found;
    }

    private static Dictionary<string, ProductBufferTotals>
        CollectInputBufferTotals(IEntityWithInputBuffersForUi entity)
    {
        var totalsByProduct = new Dictionary<string, ProductBufferTotals>(
            StringComparer.Ordinal);
        foreach (var buffer in entity.InputBuffers ??
                     Enumerable.Empty<IProductBufferReadOnly>())
        {
            if (buffer?.Product == null)
            {
                continue;
            }

            var productId = buffer.Product.Id.Value;
            if (!totalsByProduct.TryGetValue(productId, out var totals))
            {
                totals = new ProductBufferTotals
                {
                    Product = buffer.Product,
                };
                totalsByProduct[productId] = totals;
            }
            totals.Quantity += buffer.Quantity.Value;
            totals.Capacity += buffer.Capacity.Value;
        }
        return totalsByProduct;
    }

    private static int MetricSortPriority(MetricDescriptor metric)
    {
        if (metric.Path.StartsWith(
                InputProductPrefix,
                StringComparison.Ordinal))
        {
            return 0;
        }
        if (metric.Path.StartsWith(
                InputCapacityPrefix,
                StringComparison.Ordinal) ||
            metric.Path.StartsWith("external:", StringComparison.Ordinal))
        {
            return 1;
        }
        if (metric.Path.StartsWith("$", StringComparison.Ordinal))
        {
            return 2;
        }
        return 3;
    }

    private static void AddTransportMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        Transport transport)
    {
        var quantitiesByProduct = new Dictionary<ushort, double>();
        var total = 0d;
        var products = transport.TransportedProducts;
        for (var i = 0; i < products.Count; i++)
        {
            var transported = products[i];
            var quantity = transported.Quantity.Value;
            total += quantity;

            var slimId = transported.SlimId.Value;
            quantitiesByProduct.TryGetValue(slimId, out var existing);
            quantitiesByProduct[slimId] = existing + quantity;
        }

        var capacity = GetTransportCapacity(transport);
        Add(result, paths, TransportQuantityPath,
            UnmaText.Get("auto.91e00a41f65d"), total,
            UnmaText.Get("metric.units"),
            TransportCapacityPath);
        Add(result, paths, TransportCapacityPath,
            UnmaText.Get("metric.transport_capacity"), capacity,
            UnmaText.Get("metric.units"));
        Add(result, paths, TransportFillPercentPath,
            UnmaText.Get("auto.2cfde2279aa4"),
            capacity <= 0d ? 0d : total * 100d / capacity,
            "%");

        foreach (var pair in quantitiesByProduct)
        {
            if (!s_productsBySlimId.TryGetValue(pair.Key, out var product))
            {
                continue;
            }

            Add(result, paths,
                TransportProductPrefix + product.Id.Value,
                UnmaText.Get("auto.9c927c263cf2") + GetProductName(product),
                pair.Value,
                UnmaText.Get("metric.units"),
                TransportCapacityPath);
        }
    }

    private static void AddVehicleCargoMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        IVehicleCargo cargo,
        double capacity)
    {
        var total = cargo.TotalQuantity.Value;
        Add(result, paths, CargoQuantityPath,
            UnmaText.Get("metric.vehicle_cargo"), total,
            UnmaText.Get("metric.units"), CargoCapacityPath);
        Add(result, paths, CargoCapacityPath,
            UnmaText.Get("metric.vehicle_capacity"), capacity,
            UnmaText.Get("metric.units"));
        Add(result, paths, CargoFillPercentPath,
            UnmaText.Get("auto.30411f36db60"),
            capacity <= 0d ? 0d : total * 100d / capacity,
            "%");

        foreach (var pair in cargo)
        {
            Add(result, paths,
                CargoProductPrefix + pair.Key.Id.Value,
                UnmaText.Get("auto.0bca4ba56a9f") + GetProductName(pair.Key),
                pair.Value.Value,
                UnmaText.Get("metric.units"),
                CargoCapacityPath);
        }
    }

    private static void AddSingleCargoMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        ProductQuantity cargo,
        double capacity)
    {
        var total = cargo.Quantity.Value;
        Add(result, paths, CargoQuantityPath,
            UnmaText.Get("metric.vehicle_cargo"), total,
            UnmaText.Get("metric.units"), CargoCapacityPath);

        if (capacity >= 0d)
        {
            Add(result, paths, CargoCapacityPath,
                UnmaText.Get("metric.vehicle_capacity"), capacity,
                UnmaText.Get("metric.units"));
            Add(result, paths, CargoFillPercentPath,
                UnmaText.Get("auto.30411f36db60"),
                capacity <= 0d ? 0d : total * 100d / capacity,
                "%");
        }

        if (cargo.IsNotEmpty)
        {
            Add(result, paths,
                CargoProductPrefix + cargo.Product.Id.Value,
                UnmaText.Get("auto.0bca4ba56a9f") + GetProductName(cargo.Product),
                total,
                UnmaText.Get("metric.units"),
                CargoCapacityPath);
        }
    }

    private static bool TryReadTransport(
        Transport transport,
        string path,
        out double value)
    {
        value = 0d;
        switch (path)
        {
            case TransportQuantityPath:
                value = SumTransport(transport, null);
                return true;
            case TransportCapacityPath:
                value = GetTransportCapacity(transport);
                return true;
            case TransportFillPercentPath:
                var capacity = GetTransportCapacity(transport);
                value = capacity <= 0d
                    ? 0d
                    : SumTransport(transport, null) * 100d / capacity;
                return true;
        }

        if (!path.StartsWith(TransportProductPrefix, StringComparison.Ordinal) ||
            !TryGetProduct(
                path.Substring(TransportProductPrefix.Length),
                out var product))
        {
            return false;
        }

        value = SumTransport(transport, product.SlimId);
        return true;
    }

    private static bool TryReadCargo(
        IEntity entity,
        string path,
        out double value)
    {
        value = 0d;
        if (TryGetVehicleCargo(entity, out var cargo, out var capacity))
        {
            switch (path)
            {
                case CargoQuantityPath:
                    value = cargo.TotalQuantity.Value;
                    return true;
                case CargoCapacityPath:
                    value = capacity;
                    return true;
                case CargoFillPercentPath:
                    value = capacity <= 0d
                        ? 0d
                        : cargo.TotalQuantity.Value * 100d / capacity;
                    return true;
            }

            if (!path.StartsWith(CargoProductPrefix, StringComparison.Ordinal) ||
                !TryGetProduct(
                    path.Substring(CargoProductPrefix.Length),
                    out var product))
            {
                return false;
            }

            value = cargo.GetQuantityOf(product).Value;
            return true;
        }

        if (!TryGetSingleCargo(entity, out var singleCargo, out capacity))
        {
            return false;
        }

        switch (path)
        {
            case CargoQuantityPath:
                value = singleCargo.Quantity.Value;
                return true;
            case CargoCapacityPath:
                if (capacity < 0d)
                {
                    return false;
                }
                value = capacity;
                return true;
            case CargoFillPercentPath:
                if (capacity < 0d)
                {
                    return false;
                }
                value = capacity <= 0d
                    ? 0d
                    : singleCargo.Quantity.Value * 100d / capacity;
                return true;
        }

        if (!path.StartsWith(CargoProductPrefix, StringComparison.Ordinal) ||
            !TryGetProduct(
                path.Substring(CargoProductPrefix.Length),
                out var expectedProduct))
        {
            return false;
        }

        value = singleCargo.IsNotEmpty &&
                singleCargo.Product.Equals(expectedProduct)
            ? singleCargo.Quantity.Value
            : 0d;
        return true;
    }

    private static bool TryGetVehicleCargo(
        IEntity entity,
        out IVehicleCargo cargo,
        out double capacity)
    {
        switch (entity)
        {
            case Truck truck:
                cargo = truck.Cargo;
                capacity = truck.Capacity.Value;
                return true;
            case Excavator excavator:
                cargo = excavator.Cargo;
                capacity = excavator.Cargo.TotalQuantity.Value +
                           excavator.RemainingCapacity.Value;
                return true;
            case TreePlanter treePlanter:
                cargo = treePlanter.Cargo;
                capacity = treePlanter.Capacity.Value;
                return true;
            default:
                cargo = null;
                capacity = 0d;
                return false;
        }
    }

    private static bool TryGetSingleCargo(
        IEntity entity,
        out ProductQuantity cargo,
        out double capacity)
    {
        switch (entity)
        {
            case TreeHarvester treeHarvester:
                cargo = treeHarvester.Cargo;
                capacity = -1d;
                return true;
            case CargoWagon cargoWagon:
                cargo = cargoWagon.GetSubCargoWagon(0).Cargo;
                capacity = cargoWagon.Capacity.Value;
                return true;
            default:
                cargo = ProductQuantity.None;
                capacity = 0d;
                return false;
        }
    }

    private static double SumTransport(
        Transport transport,
        ProductSlimId? product)
    {
        var total = 0d;
        var products = transport.TransportedProducts;
        for (var i = 0; i < products.Count; i++)
        {
            var transported = products[i];
            if (!product.HasValue ||
                transported.SlimId.Equals(product.Value))
            {
                total += transported.Quantity.Value;
            }
        }
        return total;
    }

    private static double GetTransportCapacity(Transport transport)
    {
        if (transport.Trajectory == null || transport.Prototype == null)
        {
            return 0d;
        }

        return (double)transport.Trajectory.MaxProducts *
               transport.Prototype.MaxQuantityPerTransportedProduct.Value;
    }

    private static bool TryGetProduct(
        string productId,
        out ProductProto product)
    {
        product = null;
        return !string.IsNullOrWhiteSpace(productId) &&
               s_productsById.TryGetValue(productId, out product);
    }

    private static string GetProductName(ProductProto product)
    {
        var name = product.Strings.Name.TranslatedString;
        return string.IsNullOrWhiteSpace(name)
            ? product.Id.Value
            : name;
    }

    public static string GetEntityTitle(IEntity entity)
    {
        if (entity == null)
        {
            return UnmaText.Get("auto.cfa52542adb1");
        }

        var title = entity.DefaultTitle.Value;
        return string.IsNullOrWhiteSpace(title)
            ? entity.Prototype.Id.Value
            : title;
    }

    private static void AddNestedMetrics(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        string parentPath,
        object parent,
        int maximum)
    {
        var added = 0;
        foreach (var property in parent.GetType().GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (added >= maximum)
            {
                break;
            }
            if (!CanRead(property))
            {
                continue;
            }

            object value;
            try
            {
                value = property.GetValue(parent, null);
            }
            catch
            {
                continue;
            }

            if (!TryConvertToDouble(value, out var numeric))
            {
                continue;
            }

            var path = parentPath + "." + property.Name;
            Add(result, paths, path,
                Humanize(parentPath) + " / " + Humanize(property.Name),
                numeric);
            added++;
        }
    }

    private static bool CanRead(PropertyInfo property)
    {
        return property.CanRead &&
               property.GetIndexParameters().Length == 0 &&
               property.GetMethod != null &&
               property.GetMethod.IsPublic;
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        result = 0d;
        if (value == null)
        {
            return false;
        }

        if (value is bool boolean)
        {
            result = boolean ? 1d : 0d;
            return true;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }

        if (value is byte || value is sbyte ||
            value is short || value is ushort ||
            value is int || value is uint ||
            value is long || value is ulong ||
            value is float || value is double || value is decimal)
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }

        if (type.FullName == "Mafi.Percent")
        {
            var toDouble = type.GetMethod(
                "ToDouble",
                BindingFlags.Instance | BindingFlags.Public);
            if (toDouble != null)
            {
                result = Convert.ToDouble(
                    toDouble.Invoke(value, null),
                    CultureInfo.InvariantCulture) * 100d;
                return true;
            }
        }

        foreach (var memberName in new[] { "Value", "Count", "RawValue" })
        {
            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    var nested = property.GetValue(value, null);
                    if (nested != null && nested.GetType() != type &&
                        TryConvertToDouble(nested, out result))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Try the next representation.
                }
            }

            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                try
                {
                    var nested = field.GetValue(value);
                    if (nested != null && nested.GetType() != type &&
                        TryConvertToDouble(nested, out result))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Try the next representation.
                }
            }
        }

        foreach (var methodName in new[] { "ToDouble", "ToFloat" })
        {
            var method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
            {
                continue;
            }

            try
            {
                result = Convert.ToDouble(
                    method.Invoke(value, null),
                    CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch
            {
                // Try the next representation.
            }
        }

        return false;
    }

    private static void Add(
        ICollection<MetricDescriptor> result,
        ISet<string> paths,
        string path,
        string label,
        double value,
        string unit = "",
        string suggestedPercentReferencePath = "")
    {
        if (paths.Add(path))
        {
            result.Add(new MetricDescriptor(
                path,
                label,
                value,
                unit,
                suggestedPercentReferencePath));
        }
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var characters = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) &&
                !char.IsUpper(value[i - 1]))
            {
                characters.Add(' ');
            }
            characters.Add(current);
        }
        return new string(characters.ToArray());
    }
}
