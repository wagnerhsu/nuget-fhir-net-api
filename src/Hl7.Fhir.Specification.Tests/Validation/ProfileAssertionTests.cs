﻿using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Validation;
using System.IO;
using System.Linq;
using Xunit;
using T = System.Threading.Tasks;

namespace Hl7.Fhir.Specification.Tests
{
    public class ValidationFixture
    {
        public IResourceResolver Resolver => AsyncResolver as CachedResolver; // works, this is a CachedResolver

        public IAsyncResourceResolver AsyncResolver { get; }

        public Validator Validator { get; }
        public ValidationFixture()
        {
            AsyncResolver = new CachedResolver(
                    new MultiResolver(
                        new BasicValidationTests.BundleExampleResolver(Path.Combine("TestData", "validation")),
                        new DirectorySource(Path.Combine("TestData", "validation")),
                        new TestProfileArtifactSource(),
                        ZipSource.CreateValidationSource()));

            var ctx = new ValidationSettings()
            {
                ResourceResolver = Resolver,
                GenerateSnapshot = true,
                EnableXsdValidation = true,
                Trace = false,
                ResolveExternalReferences = true
            };


            Validator = new Validator(ctx);
        }
    }

    [Trait("Category", "Validation")]
    public class ProfileAssertionTests : IClassFixture<ValidationFixture>
    {
        private readonly IAsyncResourceResolver _asyncResolver;
        private readonly IResourceResolver _resolver;

        public ProfileAssertionTests(ValidationFixture fixture)
        {
            _resolver = fixture.Resolver;
            _asyncResolver = fixture.AsyncResolver;
        }

        [Fact]
        public async T.Task InitializationAndResolution()
        {
            var sd = await _asyncResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.ValueSet);

            var assertion = new ProfileAssertion("Patient.name[0]", resolve);
            assertion.SetInstanceType(FHIRAllTypes.ValueSet);
            assertion.SetDeclaredType(FHIRAllTypes.ValueSet);
            assertion.AddStatedProfile("http://hl7.org/fhir/StructureDefinition/shareablevalueset");

            Assert.Equal(2, assertion.AllProfiles.Count());
            Assert.Equal(2, assertion.AllProfiles.Count(p => p.Status == null));    // status == null is only true for unresolved resources

            assertion.AddStatedProfile(sd);

            Assert.Equal(2, assertion.AllProfiles.Count());         // Adding a ValueSet SD that was already there does not increase the profile count
            Assert.Equal(1, assertion.AllProfiles.Count(p => p.Status == null));    // but now there's 1 unresolved profile less
            Assert.Contains(sd, assertion.AllProfiles); // the other being the Sd we just added

            Assert.Equal(sd, assertion.InstanceType);
            Assert.Equal(sd, assertion.DeclaredType);

            var outcome = assertion.Resolve();
            Assert.True(outcome.Success);
            Assert.Equal(2, assertion.AllProfiles.Count());         // We should still have 2 distinct SDs
            Assert.Equal(0, assertion.AllProfiles.Count(p => p.Status == null));    // none remain unresolved
            Assert.Contains(sd, assertion.AllProfiles); // one still being the Sd we manually added

            assertion.AddStatedProfile("http://hl7.org/fhir/StructureDefinition/unresolvable");

            outcome = assertion.Resolve();
            Assert.False(outcome.Success);
            Assert.Equal(3, assertion.AllProfiles.Count());         // We should still have 3 distinct SDs
            Assert.Equal(1, assertion.AllProfiles.Count(p => p.Status == null));    // one remains unresolved
            Assert.Contains(sd, assertion.AllProfiles); // one still being the Sd we manually added        
        }


        // We don't want to rewrite ProfileAssertion right now...
#pragma warning disable CS0618 // Type or member is obsolete
        private StructureDefinition resolve(string uri) => _resolver.FindStructureDefinition(uri);
#pragma warning restore CS0618 // Type or member is obsolete

        [Fact]
        public void NormalElement()
        {
            var assertion = new ProfileAssertion("Patient.name[0]", resolve);
            assertion.SetDeclaredType(FHIRAllTypes.HumanName);

            Assert.True(assertion.Validate().Success);


            assertion.SetInstanceType(FHIRAllTypes.HumanName);
            Assert.True(assertion.Validate().Success);

            Assert.Single(assertion.MinimalProfiles, assertion.DeclaredType);

            assertion.SetInstanceType(FHIRAllTypes.Identifier);
            var report = assertion.Validate();
            Assert.False(report.Success);
            Assert.Contains("is incompatible with that of the instance", report.ToString());
        }

        [Fact]
        public void QuantityElement()
        {
            var assertion = new ProfileAssertion("Patient.name[0]", resolve);
            assertion.SetInstanceType(FHIRAllTypes.Age);
            assertion.SetDeclaredType(FHIRAllTypes.Quantity);

            Assert.True(assertion.Validate().Success);
            Assert.Single(assertion.MinimalProfiles, assertion.DeclaredType);

            assertion.SetInstanceType(FHIRAllTypes.Identifier);
            var report = assertion.Validate();
            Assert.False(report.Success);
            Assert.Contains("is incompatible with that of the instance", report.ToString());
        }

        [Fact]
        public void ProfiledElement()
        {
            var assertion = new ProfileAssertion("Patient.identifier[0]", resolve);
            assertion.SetDeclaredType("http://validationtest.org/fhir/StructureDefinition/IdentifierWithBSN");
            Assert.True(assertion.Validate().Success);

            assertion.SetInstanceType(FHIRAllTypes.Identifier);
            Assert.True(assertion.Validate().Success);
            Assert.Single(assertion.MinimalProfiles, assertion.DeclaredType);

            assertion.SetInstanceType(FHIRAllTypes.HumanName);
            var report = assertion.Validate();
            Assert.False(report.Success);
            Assert.Contains("is incompatible with that of the instance", report.ToString());
        }

        [Fact]
        public void ContainedResource()
        {
            var assertion = new ProfileAssertion("Bundle.entry.resource[0]", resolve);
            assertion.SetDeclaredType(FHIRAllTypes.Resource);
            Assert.True(assertion.Validate().Success);

            assertion.SetInstanceType(FHIRAllTypes.Patient);
            Assert.True(assertion.Validate().Success);

            assertion.SetDeclaredType(FHIRAllTypes.DomainResource);
            Assert.True(assertion.Validate().Success);

            Assert.Single(assertion.MinimalProfiles, assertion.InstanceType);

            assertion.SetInstanceType(FHIRAllTypes.Binary);
            var report = assertion.Validate();
            Assert.False(report.Success);
            Assert.Contains("is incompatible with that of the instance", report.ToString());
        }


        [Fact]
        public void ResourceWithStatedProfiles()
        {
            var assertion = new ProfileAssertion("Observation", resolve);
            assertion.SetDeclaredType(FHIRAllTypes.Observation);

            Assert.True(assertion.Validate().Success);

            assertion.AddStatedProfile(ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation));
            assertion.AddStatedProfile("http://validationtest.org/fhir/StructureDefinition/WeightHeightObservation");
            assertion.AddStatedProfile("http://hl7.org/fhir/StructureDefinition/devicemetricobservation");

            var report = assertion.Validate();
            Assert.True(report.Success);
            Assert.Equal(2, assertion.MinimalProfiles.Count());
            Assert.Equal(assertion.MinimalProfiles, assertion.StatedProfiles.Skip(1));

            assertion.SetDeclaredType(FHIRAllTypes.Procedure);
            report = assertion.Validate();

            Assert.False(report.Success);
            Assert.Contains("is incompatible with the declared type", report.ToString());
        }
    }

}
