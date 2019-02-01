using System;
using System.Collections.Generic;
using System.Linq;
using RuntimeInspectorNamespace;
using SolAR;
using UniRx;
using UnityEngine;
using UnityEngine.UI;
using XPCF.Api;
using XPCF.Core;

public class TestRuntimeInspector : MonoBehaviour
{
    //public RuntimeHierarchy hierarchy;
    public RuntimeInspector inspector;
    public ScrollRect scrollView;

    readonly CompositeDisposable subscription = new CompositeDisposable();

    public IComponentManager xpcfManager { get { return xpcf_api.getComponentManagerInstance(); } }

    public PipelineManager pipelineManager;

    readonly List<IComponentIntrospect> xpcfComponents = new List<IComponentIntrospect>();

    public int INT = 120;

    RectTransform drawArea;

    static UUID configurableUUID;

    GUIContent[] guiComponents;
    int idComponent = -1;
    IComponentIntrospect xpcfComponent;

    //UUID[] xpcfInterfaces;
    //GUIContent[] guiInterfaces;
    //int idInterface = -1;

    IConfigurable xpcfConfigurable;

    protected void Awake()
    {
        drawArea = scrollView.content;
        configurableUUID = configurableUUID ?? new UUID("98DBA14F-6EF9-462E-A387-34756B4CBA80");
        inspector.gameObject.SetActive(false);
    }

    protected void OnEnable()
    {
        xpcfComponents.AddRange(pipelineManager.xpcfComponents);
        guiComponents = xpcfComponents.Select(c => new GUIContent(c.GetType().Name)).ToArray();
        inspector.gameObject.SetActive(true);
    }

    protected void OnDisable()
    {
        xpcfComponents.Clear();
        inspector.gameObject.SetActive(false);
    }

    protected void OnGUI()
    {
        using (new GUILayout.HorizontalScope(GUI.skin.box, GUILayout.ExpandWidth(true)))
        {
            if (guiComponents != null)
            {
                using (GUIScope.ChangeCheck)
                {
                    idComponent = GUILayout.SelectionGrid(idComponent, guiComponents, 1, GUILayout.Width(200));
                    if (GUI.changed)
                    {
                        xpcfComponent = xpcfComponents[idComponent];
                        xpcfConfigurable = xpcfComponent.implements(configurableUUID) ? xpcfComponent.BindTo<IConfigurable>() : null;

                        Clear();
                        if (xpcfConfigurable != null)
                        {
                            foreach (var p in xpcfConfigurable.getProperties())
                            {
                                //var access = p.getAccessSpecifier();
                                var type = p.getType().ToCSharp();
                                //object value = access.CanRead() ? p.Get() : type.Default();
                                var label = p.getName();

                                var inspectedObjectDrawer = inspector.CreateDrawerForType(type, drawArea, 0);
                                if (inspectedObjectDrawer != null)
                                {
                                    inspectedObjectDrawer.BindTo(type, label, () => p.Get(), v => p.Set(v));
                                    //inspectedObjectDrawer.NameRaw = label;
                                    //inspectedObjectDrawer.Refresh();

                                    if (inspectedObjectDrawer is ExpandableInspectorField)
                                        ((ExpandableInspectorField)inspectedObjectDrawer).IsExpanded = true;

                                    Disposable.Create(inspectedObjectDrawer.Unbind).AddTo(subscription);
                                }
                            }
                        }
                    }
                }
            }
        }
        using (new GUILayout.VerticalScope(GUI.skin.box))
        {
            if (GUILayout.Button("Clear")) { Clear(); }
            if (GUILayout.Button("Button")) { Button(); }
            if (GUILayout.Button("Close")) { gameObject.SetActive(false); }
        }
    }

    void Clear()
    {
        subscription.Clear();
        inspector.StopInspect();
    }

    void Button()
    {
        var properties = new RuntimeInspectorButtonAttribute("LABEL", false, ButtonVisibility.None);
        var method = new ExposedMethod(null, properties, false);
        var methodDrawer = (ExposedMethodField)inspector.CreateDrawerForType(typeof(ExposedMethod), drawArea, 0, false);
        if (methodDrawer != null)
        {
            methodDrawer.BindTo(typeof(ExposedMethod), string.Empty, () => { Debug.Log("GET"); return null; }, (value) => Debug.Log(value));
            methodDrawer.SetBoundMethod(method);
            //methodDrawer.Refresh();

            Disposable.Create(methodDrawer.Unbind).AddTo(subscription);
        }
    }
}

public static class InspectorFieldExtensions
{
    public static void BindTo<T>(this InspectorField drawer, string name, Func<T> getter, Action<T> setter)
    {
        drawer.BindTo(typeof(T), name, () => getter(), i => setter((T)i));
    }
}
