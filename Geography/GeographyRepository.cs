﻿using GeographyModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public partial class GeographyRepository : GeographyRouter.IGeoRepository
{
    readonly Action<string> LogAction;
    public GeographyRepository(Action<string> logAction)
    {
        LogAction = logAction;
        Log("Created");
    }
    protected void Log(string message) => LogAction?.Invoke(message);

    #region Lock
    ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();
    private void WriteByLock(Action action)
    {
        Lock.EnterWriteLock();
        try
        {
            action?.Invoke();
        }
        finally { Lock.ExitWriteLock(); }
    }
    private T WriteByLock<T>(Func<T> func)
    {
        Lock.EnterWriteLock();
        try
        {
            return func.Invoke();
        }
        finally { Lock.ExitWriteLock(); }
    }
    private void ReadByLock(Action action)
    {
        Lock.EnterReadLock();
        try
        {
            action?.Invoke();
        }
        finally { Lock.ExitReadLock(); }
    }
    private T ReadByLock<T>(Func<T> func)
    {
        Lock.EnterReadLock();
        try
        {
            return func.Invoke();
        }
        finally { Lock.ExitReadLock(); }
    }
    #endregion 
    Func<Layer, bool> SaveLayer_Func; private void Save(Layer item) => SaveLayer_Func?.Invoke(item);
    Func<DomainValue, bool> SaveDomainValue_Func; private void Save(DomainValue item) => SaveDomainValue_Func?.Invoke(item);
    Func<LayerElement, bool> SaveLayerElement_Func; private void Save(LayerElement item) => SaveLayerElement_Func?.Invoke(item);

    public void BeginInitial()
    {
        layers.Clear();
        //layersMatrix.Clear();
        ElecricalMatrix = new LayerElementsMatrixByPoint(GetElement);
        //---------------------
        domains.Clear();
        //---------------------
        elements.Clear();
        elementsById.Clear();
        elementsByLayerId.Clear();
        //---------------------
        version = 0;
        versionChangeRequestStopwatch.Restart();
        //---------------------
        SaveLayer_Func = null;
        SaveDomainValue_Func = null;
        SaveLayerElement_Func = null;
    }
    public void EndInitial(Func<Layer, bool> saveLayer_Func, Func<DomainValue, bool> saveDomainValue_Func, Func<LayerElement, bool> saveLayerElement_Func)
    {
        SaveLayer_Func = saveLayer_Func;
        SaveDomainValue_Func = saveDomainValue_Func;
        SaveLayerElement_Func = saveLayerElement_Func;
    }

    #region Version
    long version = 0;
    long versionChangeRequestd = 0;
    Stopwatch versionChangeRequestStopwatch = Stopwatch.StartNew();

    public long Version => ReadByLock(() => version);
    public string VersionAsTimeText => $"{new DateTime(Version):yyyy-MM-dd HH:mm:ss.fff}";
    private void updateVersion(long versionRequestd, bool log = true)
    {
        var newVersion = Math.Max(version, versionRequestd);
        versionChangeRequestd = versionRequestd;
        versionChangeRequestStopwatch.Restart();
        if (version == newVersion) return;
        version = newVersion;
        if (log) Log($"Repository Version Changed: {version} == {new DateTime(version):yyyy-MM-dd HH:mm:ss.fff}");
    }
    public void GetVersion(out long version, out long versionRequested, out long changeElapsedMilliseconds)
    {
        long versionMiror = 0;
        long versionRequestedMiror = 0;
        long changeElapsedMillisecondsMiror = 0;
        ReadByLock(() =>
        {
            versionMiror = this.version;
            versionRequestedMiror = versionChangeRequestd;
            changeElapsedMillisecondsMiror = versionChangeRequestStopwatch.ElapsedMilliseconds;
        });
        version = versionMiror;
        versionRequested = versionRequestedMiror;
        changeElapsedMilliseconds = changeElapsedMillisecondsMiror;
    }
    #endregion Version

    #region Layers
    Dictionary<string, Layer> layers = new Dictionary<string, Layer>();
    //Dictionary<Guid, LayerElementsMatrix> layersMatrix = new Dictionary<Guid, LayerElementsMatrix>();
    LayerElementsMatrix ElecricalMatrix;
    public void Initial(List<Layer> layers)
    {
        foreach (var item in layers) update(item);
    }

    public UpdateResult Update(Layer input) => WriteByLock(() => update(input));
    public UpdateResult Update(string layercode, LayerField inputField) => WriteByLock(() =>
    {
        if (layers.ContainsKey(layercode) == false) return UpdateResult.Failed($"UpdateLayer(Code:{layercode}) not exists!");
        var layer = layers[layercode];
        var field = layer.Fields.FirstOrDefault(x => x.Code == inputField.Code);
        if (field != null)
        {
            field.Displayname = inputField.Displayname;
            field.Activation = inputField.Activation;
            field.Type = inputField.Type;
        }
        else
        {
            field = new LayerField()
            {
                Activation = inputField.Activation,
                Code = inputField.Code,
                Displayname = inputField.Displayname,
                Type = inputField.Type,
                Index = layer.Fields.Count(),
            };
            layer.Fields.Add(field);
        }

        Save(layer);
        return UpdateResult.Success();
    });

    private UpdateResult update(Layer input)
    {
        var layer = default(Layer);
        if (layers.ContainsKey(input.Code)) layer = layers[input.Code];
        else
        {
            layer = new Layer()
            {
                Id = input.Id,
                Code = input.Code,
                GeographyType = input.GeographyType,
                Fields = new List<LayerField>(),
            };
            if (layer.Id == Guid.Empty) layer.Id = Guid.NewGuid();
            layers.Add(layer.Code, layer);

            //if (layer.GeographyType == LayerGeographyType.Point || layer.GeographyType == LayerGeographyType.Polyline)
            //    layersMatrix.Add(layer.Id, new LayerElementsMatrixByPoint(GetElement));
            //else if (layer.GeographyType == LayerGeographyType.Polygon)
            //    layersMatrix.Add(layer.Id, new LayerElementsMatrixByPoint(GetElement));
            //else
            //{

            //}

        }
        layer.Activation = input.Activation;
        layer.Displayname = input.Displayname;
        layer.IsElectrical = input.IsElectrical;
        layer.IsDisconnector = input.IsDisconnector;
        layer.OperationStatusFieldCode = input.OperationStatusFieldCode;
        layer.OperationStatusOpenValue = input.OperationStatusOpenValue;
        layer.ElementDisplaynameFormat = input.ElementDisplaynameFormat;
        if (input.Fields != null)
        {
            foreach (var inputField in input.Fields)
            {
                var field = layer.Fields.FirstOrDefault(x => x.Code == inputField.Code);
                if (field != null)
                {
                    field.Displayname = inputField.Displayname;
                    field.Activation = inputField.Activation;
                    field.Type = inputField.Type;
                }
                else
                {
                    field = new LayerField()
                    {
                        Activation = inputField.Activation,
                        Code = inputField.Code,
                        Displayname = inputField.Displayname,
                        Type = inputField.Type,
                        Index = layer.Fields.Count(),
                    };
                    layer.Fields.Add(field);
                }
            }
        }
        layer.Reset(getDomain);
        Save(layer);
        return UpdateResult.Success();
    }

    public List<Layer> Layers => ReadByLock(() => layers.Values.ToList());
    public Layer GetLayer(string layercode) => ReadByLock(() =>
    {
        if (layers.ContainsKey(layercode)) return layers[layercode];
        else return null;
    });
    public List<Layer> GetLayers(IEnumerable<string> layercodes) => ReadByLock(() =>
    {
        var result = new List<Layer>();
        foreach (var layercode in layercodes)
        {
            if (layers.ContainsKey(layercode))
                result.Add(layers[layercode]);
        }
        return result;
    });
    public List<string> LayersCodes => ReadByLock(() => layers.Keys.ToList());
    public List<string> DisconnectorLayersCodes => ReadByLock(() => layers.Where(x => x.Value.IsDisconnector).Select(x => x.Key).ToList());


    public long GetLayerElementCount(string layerCode) => ReadByLock(() =>
    {
        if (layers.ContainsKey(layerCode) == false) return -1;
        else
        {
            var layer = layers[layerCode];
            if (elementsByLayerId.ContainsKey(layer.Id)) return elementsByLayerId[layer.Id].Count;
            else return 0;
        }
    });
    #endregion Layers

    #region Domains
    Dictionary<string, Domain> domains = new Dictionary<string, Domain>();
    public void Initial(List<DomainValue> domainValues)
    {
        foreach (var item in domainValues) update(item, false);
    }

    public UpdateResult Update(DomainValue input) => WriteByLock(() => update(input));// Domain.GenerateKey(layercode, fieldcode), Guid.Empty, code, value);;);
                                                                                      //private DomainValue updateDomain(string key, Guid id, long code, string value)
    private UpdateResult update(DomainValue input, bool logVersion = true)
    {
        var domain = default(Domain);
        if (domains.ContainsKey(input.DomainKey)) domain = domains[input.DomainKey];
        else
        {
            domain = new Domain(input.DomainKey);
            domains.Add(domain.Key, domain);
        }
        //--------------------------
        bool changed = false;
        var domainValue = domain.GetValue(input.Code);// default(DomainValue);
        if (domainValue == null)
        {
            domainValue = new DomainValue()
            {
                Id = input.Id,
                Activation = true,
                LayerCode = input.LayerCode,
                FieldCode = input.FieldCode,
                Code = input.Code,
                Value = "",
                Version = 0,
            };
            if (domainValue.Id == Guid.Empty) domainValue.Id = Guid.NewGuid();
            domain.Add(domainValue);
            changed |= true;
        }
        else
        {
            if (domainValue.Version >= input.Version) return UpdateResult.Failed($"UpdateDomainValue(Version passed!)");
        }
        changed |= domainValue.Activation != input.Activation;
        domainValue.Activation = input.Activation;
        changed |= domainValue.Value != input.Value;
        domainValue.Value = input.Value;

        if (domainValue.Version != input.Version)
        {
            changed |= true;
            domainValue.Version = input.Version;
            updateVersion(domainValue.Version, logVersion);
        }

        if (changed) Save(domainValue);
        return UpdateResult.Success();
    }

    public List<Domain> Domaines => ReadByLock(() => domains.Values.ToList());
    public Domain GetDomain(string key)
    {
        key = key.ToUpper().Trim();
        if (domains.ContainsKey(key)) return domains[key];
        else return null;
    }
    public Domain GetDomain(string layercode, string fieldcode) => ReadByLock(() => getDomain(layercode, fieldcode));
    public Domain getDomain(string layercode, string fieldcode)
    {
        var key = DomainValue.GenerateKey(layercode, fieldcode);
        if (domains.ContainsKey(key)) return domains[key];
        else
        {
            key = DomainValue.GenerateKey("All_Layers", fieldcode);
            if (domains.ContainsKey(key)) return domains[key];
            else return null;
        }
    }
    #endregion Domains

    #region Elements
    Dictionary<string, LayerElement> elements = new Dictionary<string, LayerElement>();
    Dictionary<Guid, LayerElement> elementsById = new Dictionary<Guid, LayerElement>();
    Dictionary<Guid, List<LayerElement>> elementsByLayerId = new Dictionary<Guid, List<LayerElement>>();

    public void Initial(string layerCode, List<LayerElement> layerElements)
    {
        if (layers.ContainsKey(layerCode) == false) return;//TODO: Register Error
        var layer = layers[layerCode];
        foreach (var item in layerElements) update(layer, item, false);
    }

    public UpdateResult Update(Layer layer, LayerElement input) => WriteByLock(() => update(layer, input));
    private UpdateResult update(Layer layer, LayerElement input, bool logVersion = true)
    {
        var element = default(LayerElement);
        if (elements.ContainsKey(input.Code))
        {
            element = elements[input.Code];
            if (element.Layer.Id != layer.Id) return UpdateResult.Failed($"UpdateElement(Layer mismatch!)");
            if (element.Version > input.Version) return UpdateResult.Failed($"UpdateElement(Version passed!)");
            //layersMatrix[element.Layer.Id].Remove(element);
            ElecricalMatrix.Remove(element);
        }
        else
        {
            element = new LayerElement()
            {
                Activation = true,
                Id = input.Id,
                Code = input.Code,
                Points = new double[] { },
                FieldValuesText = "",
                Version = 0,
            };
            element.Reset(layer);
            if (element.Id == Guid.Empty) element.Id = Guid.NewGuid();
            elements.Add(element.Code, element);
            elementsById.Add(element.Id, element);
            if (elementsByLayerId.ContainsKey(element.Layer.Id) == false) elementsByLayerId.Add(element.Layer.Id, new List<LayerElement>());
            elementsByLayerId[element.Layer.Id].Add(element);
        }
        //------------------
        element.Activation = input.Activation;
        element.Points = input.Points;
        element.FieldValuesText = input.FieldValuesText;
        element.Version = input.Version;
        updateVersion(element.Version, logVersion);

        if (element.Activation)
        {
            // layersMatrix[element.Layer.Id].Add(element);
            if (element.Layer.IsElectrical && (element.Layer.GeographyType == GeographyRouter.LayerGeographyType.Point || element.Layer.GeographyType == GeographyRouter.LayerGeographyType.Polyline))
                ElecricalMatrix.Add(element);
        }

        element.ResetDisplayname(getDomain);
        Save(element);
        return UpdateResult.Success();
    }
    public UpdateResult RemoveElement(string layercode, string elementcode, long requestVersion) => WriteByLock(() =>
    {
        if (layers.ContainsKey(layercode) == false) return UpdateResult.Failed($"RemoveElement(LayerCode:{layercode}) not exists!");
        var layer = layers[layercode];
        if (elements.ContainsKey(elementcode) == false) return UpdateResult.Failed($"RemoveElement(LayerCode:{layercode},ElementCode:{elementcode}) not exists!");
        var element = elements[elementcode];
        if (element.Layer.Id != layer.Id) return UpdateResult.Failed($"RemoveElement(Layer mismatch!)");
        if (element.Version > requestVersion) return UpdateResult.Failed($"RemoveElement(Version passed!)");
        if (element.Activation == false) return UpdateResult.Failed($"RemoveElement(Already removed!)");

        //layersMatrix[element.Layer.Id].Remove(element);
        element.Activation = false;
        Save(element);
        return UpdateResult.Success();
    });

    public int ElementCount => elements.Count;
    public LayerElement this[string code] => GetElement(code);
    public LayerElement GetElement(string code)
    {
        if (elements.ContainsKey(code)) return elements[code];
        else return null;
    }
    public List<LayerElement> this[IEnumerable<string> codes] => GetElements(codes);
    public List<LayerElement> GetElements(IEnumerable<string> codes)
    {
        var result = new List<LayerElement>();
        foreach (var code in codes.Distinct())
        {
            if (elements.ContainsKey(code))
                result.Add(elements[code]);

        }
        return result;
    }
    public LayerElement GetElement(Guid id)
    {
        if (elementsById.ContainsKey(id)) return elementsById[id];
        else return null;
    }
    public IEnumerable<LayerElement> GetElements(IEnumerable<Layer> owners, long version)
    {
        foreach (var owner in owners)
        {
            if (elementsByLayerId.ContainsKey(owner.Id) == false) continue;
            foreach (var item in elementsByLayerId[owner.Id])
            {
                if (version > item.Version) continue;
                yield return item;
            }
        }

    }
    public IEnumerable<LayerElement> GetElements(Layer owner)
    {
        if (elementsByLayerId.ContainsKey(owner.Id) == false) return new List<LayerElement>();
        return elementsByLayerId[owner.Id];
    }
    //internal IEnumerable<LayerElement> HitTest(IEnumerable<Guid> LayerIds, double Latitude, double Longitude)
    //{
    //    var result = new List<LayerElement>();
    //    foreach (var matrix in layersMatrix.Where(x => LayerIds.Contains(x.Key)))
    //        matrix.Value.HitTest(Latitude, Longitude, ref result);
    //    return result;
    //}
    #endregion Elements

    #region GeographyRouter.IGeoRepository
    public void ResetRouting() => WriteByLock(() =>
    {
        foreach (var element in elements.Values)
        {
            element.ResetRouting();
        }
    });
    public List<GeographyRouter.ILayerElement> GetRoutingSources() => ReadByLock(() =>
    {
        if (layers.ContainsKey("MVPT_HEADER") == false) return new List<GeographyRouter.ILayerElement>();
        var layer = layers["MVPT_HEADER"];
        return elementsByLayerId[layer.Id].ToList<GeographyRouter.ILayerElement>();
    });
    public void RoutingHitTest(double latitude, double longitude, ref List<GeographyRouter.ILayerElement> result, bool justNotRoute) /*=> Lock.PerformRead(() =>*/
    {
        //var result = new List<LayerElement>();
        ElecricalMatrix.HitTest(ref latitude, ref longitude, ref result, justNotRoute);

        //return result;
    }//);

    public List<string> GetNotRoutedCodes() => ReadByLock(() =>
    {
        var result = new List<string>();
        foreach (var element in elements.Values)
        {
            if (element.Layer.IsElectrical == false) continue;
            if (element.Routed) continue;
            else result.Add(element.Code);
        }
        return result;
    });
    #endregion Routing
}
