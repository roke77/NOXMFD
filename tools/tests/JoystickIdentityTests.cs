using NOXMFD;
using Xunit;

namespace NOXMFD.Tests
{
    public class JoystickIdentityTests
    {
        private static readonly (string, string)[] Joys =
        {
            ("aaa", "VPC Throttle"), ("bbb", "VPC Stick"), ("", "Pedals"), ("ccc", "VPC Stick"),
        };

        [Fact]
        public void GuidFollowsTheDeviceToItsNewNumber()
        {
            Assert.Equal(2, JoystickIdentity.Resolve(JoystickIdentity.Make("bbb", "VPC Stick"), 1, Joys));
            Assert.Equal(4, JoystickIdentity.Resolve(JoystickIdentity.Make("ccc", "VPC Stick"), 2, Joys));
        }

        [Fact]
        public void NameFallsBackWhenTheGuidIsGoneOrEmpty()
        {
            Assert.Equal(3, JoystickIdentity.Resolve(JoystickIdentity.Make("", "Pedals"), 1, Joys));
            Assert.Equal(1, JoystickIdentity.Resolve(JoystickIdentity.Make("zzz", "VPC Throttle"), 4, Joys));
        }

        [Fact]
        public void AmbiguousNameKeepsASavedNumberOfThatModel()
        {
            Assert.Equal(4, JoystickIdentity.Resolve(JoystickIdentity.Make("zzz", "VPC Stick"), 4, Joys));
            Assert.Equal(2, JoystickIdentity.Resolve(JoystickIdentity.Make("zzz", "VPC Stick"), 1, Joys));
        }

        [Fact]
        public void LearnRecordsTheDeviceAtTheSavedNumber()
        {
            Assert.Equal(JoystickIdentity.Make("bbb", "VPC Stick"), JoystickIdentity.Learn(2, Joys));
            Assert.Equal(JoystickIdentity.Make("", "Pedals"), JoystickIdentity.Learn(3, Joys));
            Assert.Equal("", JoystickIdentity.Learn(0, Joys));   // "any device" stays unpinned
            Assert.Equal("", JoystickIdentity.Learn(5, Joys));   // that stick isn't connected
        }

        [Fact]
        public void NameIsThePartAfterTheGuid()
        {
            Assert.Equal("VPC Stick", JoystickIdentity.Name(JoystickIdentity.Make("bbb", "VPC Stick")));
            Assert.Equal("Odd|Name", JoystickIdentity.Name(JoystickIdentity.Make("", "Odd|Name")));
            Assert.Equal("", JoystickIdentity.Name(""));
        }

        [Fact]
        public void UnknownOrMissingIdentityKeepsTheSavedNumber()
        {
            Assert.Equal(3, JoystickIdentity.Resolve("", 3, Joys));
            Assert.Equal(2, JoystickIdentity.Resolve(JoystickIdentity.Make("zzz", "Unplugged"), 2, Joys));
            Assert.Equal(0, JoystickIdentity.Resolve(JoystickIdentity.Make("aaa", "VPC Throttle"), 0, new (string, string)[0]));
        }
    }
}
