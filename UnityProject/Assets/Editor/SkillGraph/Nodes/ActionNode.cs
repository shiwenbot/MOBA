using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class ActionNode : SkillGraphNode
    {
        private const string ActionTypeKey = "actionType";
        private const string ValueKey = "value";
        private const string PrefabAssetPathKey = "prefabAssetPath";

        private readonly EnumField _actionTypeField;
        private readonly FloatField _valueField;
        private readonly ObjectField _prefabField;

        private SkillActionType _actionType = SkillActionType.PlayAnimation;
        private float _value = 1f;
        private GameObject _prefab;
        private string _prefabAssetPath = string.Empty;

        public ActionNode()
            : base(SkillNodeType.Action, "Action")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _actionTypeField = new EnumField("Action Type", _actionType);
            _actionTypeField.RegisterValueChangedCallback(OnActionTypeChanged);
            AddPropertyField(_actionTypeField);

            _valueField = new FloatField("Value") { value = _value };
            _valueField.RegisterValueChangedCallback(evt => _value = evt.newValue);
            AddPropertyField(_valueField);

            _prefabField = new ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            _prefabField.RegisterValueChangedCallback(OnPrefabChanged);
            AddPropertyField(_prefabField);

            UpdateActionUi();
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, ActionTypeKey, _actionType);
            AddProperty(properties, ValueKey, _value);
            AddProperty(properties, PrefabAssetPathKey, GetPrefabAssetPath());
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(GetPropertyValue(properties, ActionTypeKey, _actionType.ToString()), true, out SkillActionType actionType))
                _actionType = actionType;

            _value = GetFloatPropertyValue(properties, ValueKey, _value);
            _prefabAssetPath = SkillGraphPaths.NormalizePath(GetPropertyValue(properties, PrefabAssetPathKey, string.Empty));
            _prefab = string.IsNullOrEmpty(_prefabAssetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);

            _actionTypeField.SetValueWithoutNotify(_actionType);
            _valueField.SetValueWithoutNotify(_value);
            _prefabField.SetValueWithoutNotify(_prefab);
            UpdateActionUi();
        }

        private void OnActionTypeChanged(ChangeEvent<Enum> evt)
        {
            if (evt.newValue is SkillActionType actionType)
            {
                _actionType = actionType;
                UpdateActionUi();
            }
        }

        private void OnPrefabChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _prefab = evt.newValue as GameObject;
            _prefabAssetPath = GetPrefabAssetPath();
        }

        private string GetPrefabAssetPath()
        {
            if (_prefab != null)
                return SkillGraphPaths.NormalizePath(AssetDatabase.GetAssetPath(_prefab));

            return SkillGraphPaths.NormalizePath(_prefabAssetPath);
        }

        private void UpdateActionUi()
        {
            _valueField.label = _actionType == SkillActionType.PlayAnimation ? "Speed" : "Value";
            _prefabField.style.display = _actionType == SkillActionType.PlayAnimation
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}
