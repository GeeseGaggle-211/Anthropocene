using Anthropocene.Core;
using NUnit.Framework;
using UnityEngine;

namespace Anthropocene.Tests.EditMode
{
    public sealed class GameBootstrapTests
    {
        [Test]
        public void Bootstrap_IsAConcreteComponent()
        {
            Assert.That(typeof(GameBootstrap).IsAbstract, Is.False);
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(GameBootstrap)), Is.True);
        }
    }
}
