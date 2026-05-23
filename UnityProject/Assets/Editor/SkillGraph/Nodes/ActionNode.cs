using System;
using System.Collections.Generic;
using GameShared.SkillGraph;
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
            _valueField.RegisterValueChangedCallback(evt =>
            {
                _value = evt.newValue;
                NotifyPropertiesChanged();
            });
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
                NotifyPropertiesChanged();
            }
        }

        private void OnPrefabChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _prefab = evt.newValue as GameObject;
            _prefabAssetPath = GetPrefabAssetPath();
            NotifyPropertiesChanged();
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

    internal sealed class ApplyBuffNode : SkillGraphNode
    {
        private readonly IntegerField _buffIdField;
        private readonly IntegerField _durationFramesField;
        private readonly IntegerField _stackCountField;
        private readonly EnumField _targetSelectorField;

        private int _buffId;
        private int _durationFrames = 45;
        private int _stackCount = 1;
        private SkillBuffTargetSelector _targetSelector = SkillBuffTargetSelector.Target;

        public ApplyBuffNode()
            : base(SkillNodeType.ApplyBuff, "Apply Buff")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _targetSelectorField = new EnumField("Target", _targetSelector);
            _targetSelectorField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillBuffTargetSelector selector)
                {
                    _targetSelector = selector;
                    NotifyPropertiesChanged();
                }
            });
            AddPropertyField(_targetSelectorField);

            _buffIdField = new IntegerField("Buff Id") { value = _buffId };
            _buffIdField.RegisterValueChangedCallback(evt =>
            {
                _buffId = evt.newValue;
                NotifyPropertiesChanged();
            });
            AddPropertyField(_buffIdField);

            _durationFramesField = new IntegerField("Duration") { value = _durationFrames };
            _durationFramesField.RegisterValueChangedCallback(evt =>
            {
                _durationFrames = Math.Max(0, evt.newValue);
                _durationFramesField.SetValueWithoutNotify(_durationFrames);
                NotifyPropertiesChanged();
            });
            AddPropertyField(_durationFramesField);

            _stackCountField = new IntegerField("Stacks") { value = _stackCount };
            _stackCountField.RegisterValueChangedCallback(evt =>
            {
                _stackCount = Math.Max(1, evt.newValue);
                _stackCountField.SetValueWithoutNotify(_stackCount);
                NotifyPropertiesChanged();
            });
            AddPropertyField(_stackCountField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, RuntimePropertyKeys.TargetSelector, _targetSelector);
            AddProperty(properties, RuntimePropertyKeys.BuffId, _buffId);
            AddProperty(properties, RuntimePropertyKeys.DurationFrames, _durationFrames);
            AddProperty(properties, RuntimePropertyKeys.StackCount, _stackCount);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(
                    GetPropertyValue(properties, RuntimePropertyKeys.TargetSelector, _targetSelector.ToString()),
                    true,
                    out SkillBuffTargetSelector parsedSelector))
            {
                _targetSelector = parsedSelector;
            }

            _buffId = Math.Max(0, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.BuffId, _buffId));
            _durationFrames = Math.Max(0, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.DurationFrames, _durationFrames));
            _stackCount = Math.Max(1, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.StackCount, _stackCount));

            _targetSelectorField.SetValueWithoutNotify(_targetSelector);
            _buffIdField.SetValueWithoutNotify(_buffId);
            _durationFramesField.SetValueWithoutNotify(_durationFrames);
            _stackCountField.SetValueWithoutNotify(_stackCount);
        }
    }

    internal sealed class RemoveBuffNode : SkillGraphNode
    {
        private readonly IntegerField _buffIdField;
        private readonly EnumField _targetSelectorField;

        private int _buffId;
        private SkillBuffTargetSelector _targetSelector = SkillBuffTargetSelector.Target;

        public RemoveBuffNode()
            : base(SkillNodeType.RemoveBuff, "Remove Buff")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _targetSelectorField = new EnumField("Target", _targetSelector);
            _targetSelectorField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillBuffTargetSelector selector)
                {
                    _targetSelector = selector;
                    NotifyPropertiesChanged();
                }
            });
            AddPropertyField(_targetSelectorField);

            _buffIdField = new IntegerField("Buff Id") { value = _buffId };
            _buffIdField.RegisterValueChangedCallback(evt =>
            {
                _buffId = Math.Max(0, evt.newValue);
                _buffIdField.SetValueWithoutNotify(_buffId);
                NotifyPropertiesChanged();
            });
            AddPropertyField(_buffIdField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, RuntimePropertyKeys.TargetSelector, _targetSelector);
            AddProperty(properties, RuntimePropertyKeys.BuffId, _buffId);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(
                    GetPropertyValue(properties, RuntimePropertyKeys.TargetSelector, _targetSelector.ToString()),
                    true,
                    out SkillBuffTargetSelector parsedSelector))
            {
                _targetSelector = parsedSelector;
            }

            _buffId = Math.Max(0, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.BuffId, _buffId));
            _targetSelectorField.SetValueWithoutNotify(_targetSelector);
            _buffIdField.SetValueWithoutNotify(_buffId);
        }
    }
}
