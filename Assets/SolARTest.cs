#if !FALSE

using System;
using System.Collections.Generic;
using System.Linq;
using SolAR;
using UnityEngine;
using UnityEngine.Assertions;

public class SolARTest : AbstractSample
{
    //public Configuration conf;
    public string uuid = "5B7396F4-A804-4F3C-A0EB-FB1D56042BB4";
    SWIGTYPE_p_org__bcom__xpcf__uuids__uuid UUID { get { return SolARWrapper.toUUID(uuid); } }
    IComponentManager xpcfComponentManager;
    IComponentIntrospect xpcfComponent;
    ICamera iCamera;
    Image image;

    static SWIGTYPE_p_org__bcom__xpcf__uuids__uuid ToUUID(string uuid) { return SolARWrapper.toUUID(uuid); }

    public class KeyBasedEqualityComparer<T, TKey> : IEqualityComparer<T>
    {
        private readonly Func<T, TKey> _keyGetter;

        public KeyBasedEqualityComparer(Func<T, TKey> keyGetter)
        {
            _keyGetter = keyGetter;
        }

        public bool Equals(T x, T y)
        {
            return EqualityComparer<TKey>.Default.Equals(_keyGetter(x), _keyGetter(y));
        }

        public int GetHashCode(T obj)
        {
            TKey key = _keyGetter(obj);

            return key == null ? 0 : key.GetHashCode();
        }
    }

    protected void Start()
    {
        Extensions.modulesDict = conf.conf.modules.ToDictionary(m => m.name, m => m.uuid);
        Extensions.componentsDict = conf.conf.modules.SelectMany(m => m.components).ToDictionary(c => c.name, c => c.uuid);
        IEqualityComparer<ConfXml.Module.Component.Interface> comp = new KeyBasedEqualityComparer<ConfXml.Module.Component.Interface, string>(i => i.uuid);
        Extensions.interfacesDict = conf.conf.modules.SelectMany(m => m.components).SelectMany(c => c.interfaces).Distinct(comp).ToDictionary(i => i.name, i => i.uuid);

        image = new Image();
    }

    void DictGui(string name, Dictionary<string, string> dictionary)
    {
        using (new GUILayout.HorizontalScope(name, GUI.skin.window))
        {
            var id = GUILayout.SelectionGrid(-1, dictionary.Keys.ToArray(), 6);
            if (id != -1)
            {
                uuid = dictionary.ElementAt(id).Value;
            }
        }
    }

    bool isOpen;
    protected void OnGUI()
    {
        if (isOpen = GUILayout.Toggle(isOpen, "UUID"))
        {
            DictGui("Modules", Extensions.modulesDict);
            DictGui("Interfaces", Extensions.interfacesDict);
            DictGui("Components", Extensions.componentsDict);
        }

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("getManager"))
            {
                xpcfComponentManager = SolARWrapper.getComponentManagerInstance();
            }
            GUILayout.Toggle(xpcfComponentManager != null, "OK");
            if (GUILayout.Button("load"))
            {
                var path = conf.path;
                Debug.Log(path);
                Debug.Log(xpcfComponentManager.load(path));
            }
            if (GUILayout.Button("Dispose"))
            {
                xpcfComponentManager.Dispose();
            }
            if (GUILayout.Button("clear"))
            {
                xpcfComponentManager.clear();
            }
        }
        using (new GUILayout.HorizontalScope("Metadata", GUI.skin.window))
        {
            if (GUILayout.Button("getModulesMetadata"))
            {
                var modules = xpcfComponentManager.getModulesMetadata();
                Debug.Log(modules);
                Debug.Log(modules.size());
                var e = modules.getEnumerator();
                while (e.moveNext())
                {
                    var m = e.current();
                    Debug.LogFormat("{0}: {1} : {2}", m.name(), m.getPath(), m.description());
                }
            }
            if (GUILayout.Button("getInterfacesMetadata"))
            {
                var interfaces = xpcfComponentManager.getInterfacesMetadata();
                Debug.Log(interfaces);
                Debug.Log(interfaces.size());
                var e = interfaces.getEnumerator();
                while (e.moveNext())
                {
                    var i = e.current();
                    //Debug.Log(i.getUUID());
                    Debug.LogFormat("{0}: {1}", i.name(), i.description());
                }
            }
            if (GUILayout.Button("findComponentMetadata"))
            {
                Debug.Log(xpcfComponentManager.findComponentMetadata(UUID));
            }
            if (GUILayout.Button("findInterfaceMetadata"))
            {
                Debug.Log(xpcfComponentManager.findInterfaceMetadata(UUID));
            }
            if (GUILayout.Button("findModuleMetadata"))
            {
                Debug.Log(xpcfComponentManager.findModuleMetadata(UUID));
            }
        }
        uuid = GUILayout.TextField(uuid);
        if (GUILayout.Button("createComponent"))
        {
            xpcfComponent = xpcfComponentManager.createComponent(UUID);
        }
        GUILayout.Toggle(xpcfComponent != null, "OK");
        using (new GUILayout.HorizontalScope("IComponentIntrospect", GUI.skin.window))
        {
            if (GUILayout.Button("getNbInterfaces"))
            {
                Debug.Log(xpcfComponent.getNbInterfaces());
            }
            if (GUILayout.Button("getInterfaces"))
            {
                var interfaces = xpcfComponent.getInterfaces();
                Debug.Log(interfaces);
                /*
                Debug.Log(interfaces.size());
                var e = interfaces.getEnumerator();
                while (e.moveNext())
                {
                    var i = e.current();
                    //Debug.Log(i.getUUID());
                    Debug.LogFormat("{0}: {1}", i.name(), i.description());
                }
                */
            }
            if (GUILayout.Button("implements"))
            {
                Debug.Log(xpcfComponent.implements(UUID));
            }
            if (GUILayout.Button("getDescription"))
            {
                Assert.IsTrue(xpcfComponent.implements(UUID));
                Debug.Log(xpcfComponent.getDescription(UUID));
            }
        }
        using (new GUILayout.HorizontalScope("bindTo", GUI.skin.window))
        {
            if (GUILayout.Button("bindTo<ICamera>"))
            {
                iCamera = xpcfComponent.bindTo<ICamera>();
            }
            if (GUILayout.Button("queryInterface TODO"))
            {
                xpcfComponent = xpcfComponent.queryInterface(UUID);
            }
        }
        GUILayout.Toggle(iCamera != null, "OK");
        using (new GUILayout.HorizontalScope("ICamera", GUI.skin.window))
        {
            if (GUILayout.Button("start"))
            {
                Debug.Log(iCamera.start());
            }
            if (GUILayout.Button("getDistorsionParameters"))
            {
                Debug.Log(iCamera.getDistorsionParameters());
            }
            if (GUILayout.Button("getIntrinsicsParameters"))
            {
                Debug.Log(iCamera.getIntrinsicsParameters());
            }
            if (GUILayout.Button("getResolution"))
            {
                Debug.Log(iCamera.getResolution());
            }
            if (GUILayout.Button("getNextImage"))
            {
                Debug.Log(iCamera.getNextImage(image));
            }
        }
        GUILayout.Toggle(image != null, "OK");
        using (new GUILayout.HorizontalScope("Image", GUI.skin.window))
        {
            if (GUILayout.Button("getWidth")) Debug.Log(image.getWidth());
            if (GUILayout.Button("getHeight")) Debug.Log(image.getHeight());
            if (GUILayout.Button("getNbChannels")) Debug.Log(image.getNbChannels());
            if (GUILayout.Button("getSize")) Debug.Log(image.getSize());
            if (GUILayout.Button("getNbBitsPerComponent")) Debug.Log(image.getNbBitsPerComponent());
            if (GUILayout.Button("getBufferSize")) Debug.Log(image.getBufferSize());
            if (GUILayout.Button("getDataType")) Debug.Log(image.getDataType());
            if (GUILayout.Button("getImageLayout")) Debug.Log(image.getImageLayout());
            if (GUILayout.Button("getPixelOrder")) Debug.Log(image.getPixelOrder());
        }
    }

    protected void OnDisable()
    {
        if (iCamera != null) iCamera.Dispose();
        if (xpcfComponent != null) xpcfComponent.Dispose();
        if (xpcfComponentManager != null) xpcfComponentManager.Dispose();
    }
}
#endif
