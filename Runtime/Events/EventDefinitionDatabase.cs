using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uni7.Events
{

    [CreateAssetMenu(
        fileName = "EventDefinitionDatabase",
        menuName = "Uni7/Event Definition Database")]
    public class EventDefinitionDatabase : ScriptableObject
    {
        [SerializeField]
        public List<EventCategoryDefinition> categories = new();
    }

    [Serializable]
    public class EventCategoryDefinition
    {
        public string categoryName;
        public List<EventDefinition> events = new();
    }

    [Serializable]
    public class EventDefinition
    {
        public string eventName;

        // Event description shown in the editor and generated as XML documentation.
        [TextArea(2, 5)]
        public string comment;

        // Supported values are defined by the editor.
        public EventParameterType parameterType;
    }

    public enum EventParameterType
    {
        None,
        Int,
        Float,
        String,
        Bool,
        Vector2,
        Vector3,
        GameObject
    }
}