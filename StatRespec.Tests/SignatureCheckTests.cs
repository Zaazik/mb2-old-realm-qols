using StatRespec.Compat;
using Xunit;

namespace StatRespec.Tests
{
    public class SignatureCheckTests
    {
        private class Target
        {
            public void DoIt(int a, string b) { }
            public int Ret() => 0;
        }

        [Fact]
        public void MethodMatches_true_whenSignatureMatches()
        {
            Assert.True(SignatureCheck.MethodMatches(typeof(Target), "DoIt", typeof(void), typeof(int), typeof(string)));
            Assert.True(SignatureCheck.MethodMatches(typeof(Target), "Ret", typeof(int)));
        }

        [Fact]
        public void MethodMatches_false_whenNameMissing()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "Nope", typeof(void)));
        }

        [Fact]
        public void MethodMatches_false_whenParamsDiffer()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "DoIt", typeof(void), typeof(string)));
        }

        [Fact]
        public void MethodMatches_false_whenReturnDiffers()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "Ret", typeof(string)));
        }

        [Fact]
        public void MethodMatches_false_whenTypeNull()
        {
            Assert.False(SignatureCheck.MethodMatches(null, "DoIt", typeof(void)));
        }
    }
}
