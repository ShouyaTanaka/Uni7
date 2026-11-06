using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uni7.Diagnostics
{
    internal static class ArgumentDrawer
    {
        public static bool IsSupported(Type type)
        {
            if (type.IsEnum)
                return true;

            return type == typeof(bool) ||
                   type == typeof(string) ||
                   type == typeof(char) ||
                   type == typeof(int) ||
                   type == typeof(long) ||
                   type == typeof(float) ||
                   type == typeof(double) ||
                   type == typeof(Vector2) ||
                   type == typeof(Vector3) ||
                   type == typeof(Vector4) ||
                   type == typeof(Vector2Int) ||
                   type == typeof(Vector3Int) ||
                   type == typeof(Color) ||
                   type == typeof(Rect) ||
                   type == typeof(Bounds) ||
                   type == typeof(Quaternion) ||
                   typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        public static object GetDefault(Type type)
        {
            if (type == typeof(string)) return string.Empty;
            if (type == typeof(char)) return '\0';
            if (type == typeof(bool)) return false;
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public static VisualElement CreateField(string label, Type type, Action<object> onChanged)
        {
            if (type == typeof(bool))
            {
                var field = new Toggle(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(string))
            {
                var field = new TextField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(char))
            {
                var field = new TextField(label);
                field.maxLength = 1;
                field.RegisterValueChangedCallback(e => onChanged(string.IsNullOrEmpty(e.newValue) ? '\0' : e.newValue[0]));
                return field;
            }

            if (type == typeof(int))
            {
                var field = new IntegerField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(long))
            {
                var field = new LongField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(float))
            {
                var field = new FloatField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(double))
            {
                var field = new DoubleField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type.IsEnum)
            {
                var field = new EnumField(label, (Enum)GetDefault(type));
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Vector2))
            {
                var field = new Vector2Field(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Vector3))
            {
                var field = new Vector3Field(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Vector4))
            {
                var field = new Vector4Field(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Vector2Int))
            {
                var field = new Vector2IntField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Vector3Int))
            {
                var field = new Vector3IntField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Color))
            {
                var field = new ColorField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Rect))
            {
                var field = new RectField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Bounds))
            {
                var field = new BoundsField(label);
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            if (type == typeof(Quaternion))
            {
                var field = new Vector4Field(label);
                field.RegisterValueChangedCallback(e =>
                    onChanged(new Quaternion(e.newValue.x, e.newValue.y, e.newValue.z, e.newValue.w)));
                return field;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var field = new ObjectField(label)
                {
                    objectType = type,
                    allowSceneObjects = true
                };
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return field;
            }

            return new Label($"{label}: Unsupported ({type.Name})");
        }
    }
}
