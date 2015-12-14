﻿/* Copyright 2015 Realm Inc - All Rights Reserved
 * Proprietary and Confidential
 */
 
using NUnit.Framework;
using Realms;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

// NOTE some of the following data comes from Tim's data used in the Browser screenshot in the Mac app store
// unlike the Cocoa definitions, we use Pascal casing for properties
namespace IntegrationTests.Shared
{
    [TestFixture]
    public class RelationshipTests
    {
        class Dog : RealmObject
        {
            public string Name { get; set; }
            public string Color { get; set; } = "Brown";
            public bool Vaccinated { get; set; } = true;
            //Owner Owner { get; set; }  will uncomment when verifying that we have back-links from ToMany relationships
        }

        class Owner : RealmObject
        {
            public string Name { get; set; }
            public Dog TopDog { get; set; }
            public RealmList<Dog> Dogs { get; set; } 
        }

        protected Realm realm;

        [SetUp]
        public void Setup()
        {

            realm = Realm.GetInstance(Path.GetTempFileName());

            // we don't keep any variables pointing to these as they are all added to Realm
            using (var trans = realm.BeginWrite())
            {
                /* syntax we want back needs ability for constructor to auto-bind to active write transaction
                new Owner {Name = "Tim", Dogs = new RealmList<Dog> {
                    new Dog {Name = "Bilbo Fleabaggins"},
                    new Dog {Name = "Earl Yippington III" }
                    } };
                    */
                Owner o1 = realm.CreateObject<Owner> ();
                o1.Name = "Tim";

                Dog d1 = realm.CreateObject<Dog> ();
                d1.Name = "Bilbo Fleabaggins";
                d1.Color = "Black";
                o1.TopDog = d1;  // set a one-one relationship
                o1.Dogs.Add (d1);

                Dog d2 = realm.CreateObject<Dog> ();
                d2.Name = "Earl Yippington III";
                d2.Color = "White";
                o1.Dogs.Add (d2);

                // lonely people and dogs
                Owner o2 = realm.CreateObject<Owner> ();
                o2.Name = "Dani";  // the dog-less

                Dog d3 = realm.CreateObject<Dog> ();  // will remain unassigned
                d3.Name = "Maggie Mongrel";
                d3.Color = "Grey";

                /*
                These would work if we can preserve init through weaving, like:
                public RealmList<Dog> Dogs { get; set; } = new RealmList<Dog>();

                new Owner {Name = "JP", Dogs = { new Dog { Name = "Deputy Dawg", Vaccinated=false } } };
                new Owner {Name = "Arwa", Dogs = { new Dog { Name = "Hairy Pawter", Color = "Black" } } };
                new Owner {Name = "Joe", Dogs = { new Dog { Name = "Jabba the Mutt", Vaccinated = false } } };
                new Owner {Name = "Alex", Dogs = { new Dog { Name = "Hairy Pawter", Color = "Black" } } };
                new Owner {Name = "Michael", Dogs = { new Dog { Name = "Nerf Herder", Color="Red" } } };
                new Owner {Name = "Adam", Dogs = { new Dog { Name = "Defense Secretary Waggles" } } };
                new Owner {Name = "Samuel", Dogs = { new Dog { Name = "Salacious B. Crumb", Color="Tan" } } };
                new Owner {Name = "Kristen"}; // Kristen's dog was abducted by Tim so she doesn't have any
                new Owner {Name = "Emily", Dogs = { new Dog { Name = "Pickles McPorkchop" } } };
                new Owner {Name = "Katsumi", Dogs = { new Dog { Name = "Sir Yaps-a-lot", Vaccinated = false } } };
                new Owner {Name = "Morgan", Dogs = { new Dog { Name = "Rudy Loosebooty" } } };
                */
                // to show you can assign later, create the Owner then assign their Dog

                    /* syntax for later
                var b = new Owner {Name = "Bjarne"};  
                b.Dogs = new RealmList<Dog> { new Dog { Name = "Madame Barklouder", Vaccinated = false, Color = "White" }};
                */
                trans.Commit ();
            }
        }


        [Test]
        public void TimHasATopDog()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.TopDog.Name, Is.EqualTo( "Bilbo Fleabaggins"));
        }


        [Test]
        public void TimHasTwoIterableDogs()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            var dogNames = new List<string>();
            foreach (var dog in tim.Dogs)
            {
                dogNames.Add(dog.Name);
            }
            Assert.That(dogNames, Is.EquivalentTo( new List<String> {"Bilbo Fleabaggins", "Earl Yippington III"}));
        }


        [Test]
        public void TimRetiredHisTopDog()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            using (var trans = realm.BeginWrite()) {
                tim.TopDog = null;
                trans.Commit ();
            }                
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.TopDog, Is.Null);  // the dog departure was saved
        }


        [Test]
        public void TimAddsADogLater()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.Dogs.Count(), Is.EqualTo(2));  
            using (var trans = realm.BeginWrite()) {
                var dog3 = realm.All<Dog>().Where( p => p.Name == "Maggie Mongrel").ToList().First();
                tim.Dogs.Add (dog3);
                trans.Commit ();
            }
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(3));  
            Assert.That(tim2.Dogs[2].Name, Is.EqualTo("Maggie Mongrel")); 
        }


        [Test]
        public void TimAddsADogByInsert()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.Dogs.Count(), Is.EqualTo(2));  
            using (var trans = realm.BeginWrite()) {
                var dog3 = realm.All<Dog>().Where( p => p.Name == "Maggie Mongrel").ToList().First();
                tim.Dogs.Insert (1, dog3);
                trans.Commit ();
            }
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(3));  
            Assert.That(tim2.Dogs[1].Name, Is.EqualTo("Maggie Mongrel")); 
            Assert.That(tim2.Dogs[2].Name, Is.EqualTo("Earl Yippington III")); 
        }


        [Test]
        public void TimLosesHisDogsByOrder()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.Dogs.Count(), Is.EqualTo(2));  
            using (var trans = realm.BeginWrite()) {
                tim.Dogs.RemoveAt(0);
                trans.Commit ();
            }                
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(1)); 
            Assert.That(tim2.Dogs[0].Name, Is.EqualTo("Earl Yippington III")); 
            using (var trans = realm.BeginWrite()) {
                tim.Dogs.RemoveAt(0);
                trans.Commit ();
            }                
            var tim3 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(0)); 
            Assert.That(tim3.Dogs.Count(), Is.EqualTo(0)); // reloaded object has same empty related set
        }


        [Test]
        public void TimLosesHisDogsInOneClear()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.Dogs.Count(), Is.EqualTo(2));  
            using (var trans = realm.BeginWrite()) {
                tim.Dogs.Clear();
                trans.Commit ();
            }                
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(0)); 
        }


        [Test]
        public void TimLosesBilbo()
        {
            var bilbo = realm.All<Dog>().Where( p => p.Name == "Bilbo Fleabaggins").ToList().First();
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim.Dogs.Count(), Is.EqualTo(2));  
            using (var trans = realm.BeginWrite()) {
                tim.Dogs.Remove(bilbo);
                trans.Commit ();
            }                
            var tim2 = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.That(tim2.Dogs.Count(), Is.EqualTo(1)); 
            Assert.That(tim2.Dogs[0].Name, Is.EqualTo("Earl Yippington III")); 
        }


        [Test]
        public void DaniHasNoTopDog()
        {
            var dani = realm.All<Owner>().Where( p => p.Name == "Dani").ToList().First();
            Assert.That(dani.TopDog, Is.Null);
        }


        [Test]
        public void DaniHasNoDogs()
        {
            var dani = realm.All<Owner>().Where( p => p.Name == "Dani").ToList().First();
            Assert.That(dani.Dogs.Count(), Is.EqualTo(0));  // ToMany relationships always return a RealmList
            int dogsIterated = 0; 
            foreach (var d in dani.Dogs)
            {
                dogsIterated++;
            }
            Assert.That(dogsIterated, Is.EqualTo(0));
        }


        [Test]
        public void TestExceptionsFromEmptyListOutOfRange()
        {
            var dani = realm.All<Owner>().Where(p => p.Name == "Dani").ToList().First();
            Assert.Throws<IndexOutOfRangeException>(() => dani.Dogs.RemoveAt(0));
            var bilbo = realm.All<Dog>().Where(p => p.Name == "Bilbo Fleabaggins").ToList().First();
            Dog scratch;  // for assignment in following getters
            Assert.Throws<IndexOutOfRangeException>(() => dani.Dogs.Insert(-1, bilbo));
            Assert.Throws<IndexOutOfRangeException>(() => dani.Dogs.Insert(0, bilbo));
            Assert.Throws<IndexOutOfRangeException>(() => scratch = dani.Dogs[0]);
        }


        [Test]
        public void TestExceptionsFromIteratingEmptyList()
        {
            var dani = realm.All<Owner>().Where(p => p.Name == "Dani").ToList().First();
            var iter =  dani.Dogs.GetEnumerator();
            Assert.IsNotNull(iter);
            var movedOnToFirstItem = iter.MoveNext();
            Assert.That(movedOnToFirstItem, Is.False);
            Dog currentDog;
            Assert.Throws<IndexOutOfRangeException>(() => currentDog = iter.Current );
        }


        [Test]
        public void TestExceptionsFromTimsDogsOutOfRange()
        {
            var tim = realm.All<Owner>().Where( p => p.Name == "Tim").ToList().First();
            Assert.Throws<IndexOutOfRangeException>( () => tim.Dogs.RemoveAt(4) );
            var bilbo = realm.All<Dog>().Where( p => p.Name == "Bilbo Fleabaggins").ToList().First();
            Dog scratch;  // for assignment in following getters
            Assert.Throws<IndexOutOfRangeException>( () => tim.Dogs.Insert(-1, bilbo) );
            Assert.Throws<IndexOutOfRangeException>( () => tim.Dogs.Insert(3, bilbo) );
            Assert.Throws<IndexOutOfRangeException>( () => scratch = tim.Dogs[99] );
        }

        [Test]
        public void TestSettingStandAloneObjectToRelationship()
        {
            var owner = realm.All<Owner>().ToList().First();
            var dog = new Dog { Name = "Astro" };
            
            using (var trans = realm.BeginWrite())
            {
                owner.TopDog = dog;
                trans.Commit();
            }

            var dogAgain = realm.All<Dog>().Where(d => d.Name == "Astro").ToList().SingleOrDefault();
            Assert.That(dogAgain, Is.Not.Null);
            Assert.That(dog.IsManaged);
        }

        [Test]
        public void TestAddingStandAloneObjectToToManyRelationship()
        {
            var owner = realm.All<Owner>().ToList().First();
            var dog = new Dog { Name = "Astro" };

            using (var trans = realm.BeginWrite())
            {
                owner.Dogs.Add(dog);
                trans.Commit();
            }

            var dogAgain = realm.All<Dog>().Where(d => d.Name == "Astro").ToList().SingleOrDefault();
            Assert.That(dogAgain, Is.Not.Null);
            Assert.That(dog.IsManaged);
        }

        [Test]
        public void TestAttachingStandaloneTwoLevelRelationship()
        {
            var person = new Person
            {
                FullName = "Person 0",
                Friends =
                {
                    new Person { FullName = "Friend A" },
                    new Person { FullName = "Friend B" }
                }
            };

            Assert.That(person.Friends is List<Person>);

            using (var trans = realm.BeginWrite())
            {
                realm.Attach(person);
                trans.Commit();
            }

            Assert.That(person.Friends is RealmList<Person>);
            Assert.That(realm.All<Person>().ToList().Count, Is.EqualTo(3));
        }


        [Test]
        public void TestAttachingStandaloneThreeLevelRelationship()
        {
            var sally = new Person
            {
                FullName = "Sally",
                Friends =
                {
                    new Person { FullName = "Alice" },
                    new Person
                    {
                        FullName = "Joan",
                        Friends =
                        {
                            new Person()
                            {
                                FullName = "Krystal",
                                Friends = { new Person {  FullName = "Sally"} }  // attaches a second Sally
                            }
                        } 

                    }
                }
            };

            using (var trans = realm.BeginWrite()) {
                realm.Attach(sally);  // top person attaches entire tree
                trans.Commit();
            }

            Assert.That(realm.All<Person>().ToList().Count, Is.EqualTo(5));
            Assert.That(realm.All<Person>().Where(p => p.FirstName=="Sally").ToList().Count, Is.EqualTo(2));
        }


        [Test]
        public void TestCircularRelationshipsFromStandaloneTwoStage()
        {
            var sally = new Person
            {
                FullName = "Sally",
                Friends =
                {
                    new Person { FullName = "Alice" },
                    new Person { FullName = "Joan"  }       
                }
            };
            var joanFriend = new Person()
            {
                FullName = "Krystal",
                Friends = {sally} 
            };

            sally.Friends[1].Friends.Add(joanFriend);
            using (var trans = realm.BeginWrite()) {
                realm.Attach(sally);  // top person attaches entire tree
                trans.Commit();
            }

            Assert.That(realm.All<Person>().ToList().Count, Is.EqualTo(4));
            Assert.That(realm.All<Person>().Where(p => p.FirstName=="Sally").ToList().Count, Is.EqualTo(1));
        }

    }
}