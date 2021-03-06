﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Mocks;

namespace Symphony.Core
{
    using NUnit.Framework;

    [TestFixture]
    public class ControllerTests
    {
        private const string UNUSED_DEVICE_NAME = "TEST-DEVICE";
        private const string UNUSED_DEVICE_MANUFACTURER = "TEST-DEVICE-CO";
        private const string UNUSED_STREAM_NAME = "TEST-STREAM";
        Measurement UNUSED_BACKGROUND = new Measurement(0, "V");
        
        [Test]
        [Timeout(1000)]
        public void PullOutputDataShouldReturnBackgroundStreamWithNoRemainingEpochs()
        {
            var daq = new SimpleDAQController();
            Controller c = new NonValidatingController() { DAQController = daq };
            IExternalDevice dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND);
            dev.BindStream(new DAQOutputStream("out"));

            var srate = new Measurement(1000, "Hz");
            var background = new Background(new Measurement(1, "V"), srate);
            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(background);

            TimeSpan dur = TimeSpan.FromSeconds(0.5);
            var samples = (int)dur.Samples(srate);

            var e = new Epoch("");
            e.Stimuli[dev] = new RenderedStimulus("RenderedStimulus", new Dictionary<string, object>(), new OutputData(Enumerable.Repeat(new Measurement(0, "V"), samples), srate));

            IOutputData pull1 = null;
            IOutputData pull2 = null;
            bool pulled = false;
            daq.Started += (evt, args) =>
            {
                // Pull out epoch data
                c.PullOutputData(dev, dur);

                pull1 = c.PullOutputData(dev, dur);
                pull2 = c.PullOutputData(dev, dur);
                pulled = true;

                c.RequestStop();
            };
            
            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!pulled)
            {
                Thread.Sleep(1);
            }

            var expected = Enumerable.Repeat(background.Value, samples).ToList();

            Assert.AreEqual(expected, pull1.Data);
            Assert.AreEqual(expected, pull2.Data);
        }


        [Test]
        public void ControllerImplementsTimelineProducer()
        {
            Assert.IsInstanceOf<ITimelineProducer>(new Controller());
        }

        [Test]
        public void SimpleCreation()
        {
            Controller controller = new Controller();
            Assert.IsNotNull(controller);
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof(InvalidOperationException))]
        public void PreventsDuplicateExternalDeviceNames()
        {
            Controller c = new Controller();

            const string UNUSED_NAME = "UNUSED";
            var dev1 = new UnitConvertingExternalDevice(UNUSED_NAME, UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND);
            var dev2 = new UnitConvertingExternalDevice(UNUSED_NAME, UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND);

            c.AddDevice(dev1).AddDevice(dev2);
        }

        [Test]
        public void GetDevicesReturnsNullForUnknownDevice()
        {
            Controller c = new Controller();

            const string UNUSED_NAME = "UNUSED";
            Assert.IsNull(c.GetDevice(UNUSED_NAME));
        }

        [Test]
        public void GetDeviceReturnsDevice()
        {
            Controller c = new Controller();

            const string DEVICE_NAME = "DEVICE";
            var dev1 = new UnitConvertingExternalDevice(DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND);

            c.AddDevice(dev1);

            Assert.AreEqual(dev1, c.GetDevice(DEVICE_NAME));
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof(Exception),
            ExpectedMessage = "Unrecognized Measurement conversion: inches to feet")]
        public void UnknownMeasurementConversionRaisesException()
        {
            // The controller has nothing in its conversions dictionary to start
            // so therefore this should throw an exception
            Controller controller = new Controller();

            Measurement tedsHeight = new Measurement(74, "inches");
            IMeasurement tedsHeightInFeet = Converters.Convert(tedsHeight, "feet");
        }

        [Test]
        public void ExternalDeviceStreams()
        {
            var ed = new UnitConvertingExternalDevice("TestDevice", UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND);

            DAQInputStream in0 = new DAQInputStream("In-0");
            DAQOutputStream out0 = new DAQOutputStream("Out-0");
            ed.BindStream(in0).BindStream(out0);

            Assert.IsTrue(ed.Streams.ContainsKey(in0.Name));
            Assert.IsTrue(in0.Devices.Contains(ed));
            Assert.IsTrue(ed.Streams.ContainsKey(out0.Name));
            Assert.IsTrue(out0.Device == ed);

            ed.UnbindStream("In-0");
            Assert.IsFalse(ed.Streams.ContainsKey(in0.Name));
            Assert.IsFalse(in0.Devices.Contains(ed));
            Assert.IsTrue(ed.Streams.ContainsKey(out0.Name));
            Assert.IsTrue(out0.Device == ed);

            ed.UnbindStream("Out-0");
            Assert.IsFalse(ed.Streams.ContainsKey(in0.Name));
            Assert.IsFalse(in0.Devices.Contains(ed));
            Assert.IsFalse(ed.Streams.ContainsKey(out0.Name));
            Assert.IsFalse(out0.Device == ed);
        }

        [Test]
        public void CreatePipeline()
        {


            // Based on the "Minimal Rig.pdf" in the docs folder
            Converters.Clear();

            // We need an IClock
            IClock clock = new FakeClock();

            // We need a controller ...
            Controller con = new Controller();

            Converters.Register("units", "units",
                // just an identity conversion for now, to pass Validate()
                (IMeasurement m) => m);
            Converters.Register("V", "units",
                // just an identity conversion for now, to pass Validate()
               (IMeasurement m) => m);

            con.Clock = clock;

            var sampleRate = new Measurement(10000, "Hz");

            // Three ExternalDevices
            CoalescingDevice amp = new CoalescingDevice("Amp", UNUSED_DEVICE_MANUFACTURER, con, UNUSED_BACKGROUND)
                                       {
                                           MeasurementConversionTarget = "units",
                                           OutputSampleRate = sampleRate,
                                           InputSampleRate = sampleRate
                                       };
            var LED = new UnitConvertingExternalDevice("LED", UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND)
            {
                MeasurementConversionTarget = "units",
                OutputSampleRate = sampleRate
            };
            var temp = new UnitConvertingExternalDevice("Temp", UNUSED_DEVICE_MANUFACTURER, UNUSED_BACKGROUND)
            {
                MeasurementConversionTarget = "units",
                InputSampleRate = sampleRate
            };
            amp.Clock = clock;
            LED.Clock = clock;
            temp.Clock = clock;
            con.AddDevice(LED).AddDevice(temp);
            // There should be no difference whether we use the
            // ExternalDevice constructor to wire up the Controller
            // to the ExternalDevice, or the explicit Add() call

            Assert.IsNotNull(amp.Controller);
            Assert.IsNotNull(LED.Controller);
            Assert.IsNotNull(temp.Controller);

            Assert.IsTrue(amp.Controller == con);
            Assert.IsTrue(LED.Controller == con);
            Assert.IsTrue(temp.Controller == con);

            // Five DAQStreams
            DAQInputStream in0 = new DAQInputStream("In-0"); in0.Clock = clock;
            DAQInputStream in1 = new DAQInputStream("In-1"); in1.Clock = clock;
            DAQInputStream in2 = new DAQInputStream("In-2"); in2.Clock = clock;
            DAQOutputStream out0 = new DAQOutputStream("Out-0"); out0.Clock = clock;
            DAQOutputStream out1 = new DAQOutputStream("Out-1"); out1.Clock = clock;
            in0.MeasurementConversionTarget = "units";
            in1.MeasurementConversionTarget = "units";
            in2.MeasurementConversionTarget = "units";
            out0.MeasurementConversionTarget = "units";
            out1.MeasurementConversionTarget = "units";

            //amp.Coalesce = CoalescingDevice.OneItemCoalesce;
            amp.Configuration["CoalesceProc"] = "Symphony.Core.CoalescingDevice.OneItemCoalesce";

            LED.BindStream(out0);
            amp.BindStream(out1).BindStream(in0).BindStream(in1);
            amp.Connect(in0, in1);
            temp.BindStream(in2);

            Assert.IsTrue(LED.Streams.Count == 1);
            Assert.IsTrue(amp.Streams.Count == 3);
            Assert.IsTrue(temp.Streams.Count == 1);

            Assert.IsTrue(in0.Devices.Contains(amp));
            Assert.IsTrue(in1.Devices.Contains(amp));
            Assert.IsTrue(in2.Devices.Contains(temp));
            Assert.IsTrue(out0.Device == LED);
            Assert.IsTrue(out1.Device == amp);

            // One DAQController
            IDAQController dc =
                new SimpleDAQController(new IDAQStream[] { in0, in1, in2, out0, out1 });

            con.DAQController = dc;

            // DAQController-to-streams
            Assert.IsTrue(dc.InputStreams.Contains(in0));
            Assert.IsTrue(dc.InputStreams.Contains(in1));
            Assert.IsTrue(dc.InputStreams.Contains(in2));
            Assert.IsTrue(dc.OutputStreams.Contains(out0));
            Assert.IsTrue(dc.OutputStreams.Contains(out0));

            // We need to associate a background stream with each output device
            con.BackgroundDataStreams[LED] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, new Measurement(1, "Hz")));
            con.BackgroundDataStreams[amp] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, new Measurement(1, "Hz")));

            // Validate and report the validation results
            Maybe<string> conVal = con.Validate();
            Assert.IsTrue(conVal, conVal.Item2);

            Assert.IsTrue(amp.Coalesce == CoalescingDevice.OneItemCoalesce);
        }

        [Test]
        public void EnumeratesHardwareControllers()
        {
            Controller controller = new Controller();
            IDAQController daq = new SimpleDAQController();
            controller.DAQController = daq;

            Assert.AreEqual(new IHardwareController[] { daq } as IEnumerable<IHardwareController>, controller.HardwareControllers);
        }

        [Test, Timeout(2000)]
        public void PullsOutputData()
        {
            var daq = new SimpleDAQController();
            var c = new NonValidatingController { DAQController = daq };
            var dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND);
            dev.BindStream(new DAQOutputStream("out"));

            const int srate = 1000;
            IList<IMeasurement> data = (IList<IMeasurement>) Enumerable.Range(0, srate * 2).Select(i => new Measurement(i, "V") as IMeasurement).ToList();
            var sampleRate = new Measurement(srate, "Hz");

            var background = new Background(new Measurement(3, "V"), sampleRate);
            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(background);

            IOutputData data1 = new OutputData(data, sampleRate, false);

            var e = new Epoch("");
            e.Stimuli[dev] = new RenderedStimulus((string) "RenderedStimulus", (IDictionary<string, object>) new Dictionary<string, object>(),
                data1);

            TimeSpan d1 = TimeSpan.FromSeconds(0.75);

            IOutputData pull1 = null;
            IOutputData pull2 = null;
            IOutputData pull3 = null;
            bool pulled = false;
            daq.Started += (evt, args) =>
            {
                pull1 = c.PullOutputData(dev, d1);
                pull2 = c.PullOutputData(dev, d1);
                pull3 = c.PullOutputData(dev, d1);
                pulled = true;

                c.RequestStop();
            };

            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!pulled)
            {
                Thread.Sleep(1);
            }

            var samples = (int)d1.Samples(new Measurement(srate, "Hz"));
            Assert.AreEqual(data.Take(samples).ToList(),
                pull1.Data);
            Assert.AreEqual(data.Skip(samples).Take(samples).ToList(),
                pull2.Data);
            Assert.AreEqual(data.Skip(2 * samples)
                .Take(samples)
                .Concat(Enumerable.Range(0, srate - samples).Select(i => background.Value))
                .ToList(),
                pull3.Data);
        }

        [Test]
        public void ShouldPersistCompletedEpochs()
        {
            Converters.Register("V", "V",
                (IMeasurement m) => m);

            var c = new Controller();
            bool evt = false;

            c.DAQController = new TestDAQController();
            c.Clock = c.DAQController as IClock;
            var persistor = new FakeEpochPersistor();

            c.SavedEpoch += (co, args) =>
                                {
                                    evt = true;
                                };

            var srate = new Measurement(10, "Hz");

            var dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND)
                          {
                              MeasurementConversionTarget = "V",
                              Clock = c.Clock,
                              OutputSampleRate = srate,
                              InputSampleRate = srate
                          };

            var outStream = new DAQOutputStream("outStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            var inStream = new DAQInputStream("inStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            (c.DAQController as IMutableDAQController).AddStream(outStream);
            (c.DAQController as IMutableDAQController).AddStream(inStream);

            outStream.SampleRate = srate;
            inStream.SampleRate = srate;

            dev.BindStream(outStream);
            dev.BindStream(inStream);

            var e = new Epoch(UNUSED_PROTOCOL);
            var samples = new List<IMeasurement> { new Measurement(1.0m, "V"), new Measurement(1.0m, "V"), new Measurement(1.0m, "V") };
            var data = new OutputData(samples,
                srate,
                true);

            e.Stimuli[dev] = new RenderedStimulus((string) "stimID",
                                                  (IDictionary<string, object>) new Dictionary<string, object>(),
                                                  (IOutputData) data);
            e.Responses[dev] = new Response();
            e.Backgrounds[dev] = new Background(new Measurement(0, "V"), srate);

            var back = new Background(UNUSED_BACKGROUND, srate);
            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(back);

            ((TestDAQController)c.DAQController).AddStreamMapping(outStream, inStream);

            c.RunEpoch(e, persistor);

            Assert.That(evt, Is.True.After(10*1000,100));
            Assert.That(persistor.PersistedEpochs, Has.Member(e));
        }

        [Test]
        public void ShouldNotPersistEpochWithShouldBePersistedSetFalse()
        {
            Converters.Register("V", "V",
                (IMeasurement m) => m);

            var c = new Controller();
            bool evt = false;

            c.DAQController = new TestDAQController();
            c.Clock = c.DAQController as IClock;
            var persistor = new FakeEpochPersistor();

            c.SavedEpoch += (co, args) =>
            {
                evt = true;
            };

            var srate = new Measurement(10, "Hz");

            var dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND)
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock,
                OutputSampleRate = srate,
                InputSampleRate = srate
            };

            var outStream = new DAQOutputStream("outStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            var inStream = new DAQInputStream("inStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            (c.DAQController as IMutableDAQController).AddStream(outStream);
            (c.DAQController as IMutableDAQController).AddStream(inStream);

            outStream.SampleRate = srate;
            inStream.SampleRate = srate;

            dev.BindStream(outStream);
            dev.BindStream(inStream);

            var e = new Epoch(UNUSED_PROTOCOL) { ShouldBePersisted = false };

            var samples = new List<IMeasurement> { new Measurement(1.0m, "V"), new Measurement(1.0m, "V"), new Measurement(1.0m, "V") };
            var data = new OutputData(samples,
                srate,
                true);

            e.Stimuli[dev] = new RenderedStimulus((string)"stimID",
                                                  (IDictionary<string, object>)new Dictionary<string, object>(),
                                                  (IOutputData)data);
            e.Responses[dev] = new Response();
            e.Backgrounds[dev] = new Background(new Measurement(0, "V"), srate);

            var back = new Background(UNUSED_BACKGROUND, srate);
            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(back);

            ((TestDAQController)c.DAQController).AddStreamMapping(outStream, inStream);

            c.RunEpoch(e, persistor);

            Assert.That(evt, Is.False.After(10 * 1000, 100));
            Assert.That(persistor.PersistedEpochs, Is.Empty);
        }

        [Test]
        public void ShouldSurfaceExceptionInPersistorTask()
        {
            Converters.Register("V", "V",
                (IMeasurement m) => m);

            var c = new Controller();
            bool evt = false;

            c.DAQController = new TestDAQController();
            c.Clock = c.DAQController as IClock;
            var persistor = new AggregateExceptionThrowingEpochPersistor();

            c.SavedEpoch += (co, args) =>
            {
                evt = true;
            };

            var srate = new Measurement(10, "Hz");

            var dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND)
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock,
                OutputSampleRate = srate,
                InputSampleRate = srate
            };

            var outStream = new DAQOutputStream("outStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            var inStream = new DAQInputStream("inStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            (c.DAQController as IMutableDAQController).AddStream(outStream);
            (c.DAQController as IMutableDAQController).AddStream(inStream);

            outStream.SampleRate = srate;
            inStream.SampleRate = srate;

            dev.BindStream(outStream);
            dev.BindStream(inStream);

            var back = new Background(UNUSED_BACKGROUND, srate);
            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(back);

            var e = new Epoch(UNUSED_PROTOCOL);
            var samples = new List<IMeasurement> { new Measurement(1.0m, "V"), new Measurement(1.0m, "V"), new Measurement(1.0m, "V") };
            var data = new OutputData(samples,
                srate,
                true);

            e.Stimuli[dev] = new RenderedStimulus((string) "stimID",
                                                  (IDictionary<string, object>) new Dictionary<string, object>(),
                                                  (IOutputData) data);
            e.Responses[dev] = new Response();
            e.Backgrounds[dev] = new Background(new Measurement(0, "V"), srate);

            ((TestDAQController)c.DAQController).AddStreamMapping(outStream, inStream);

            Assert.That(() => c.RunEpoch(e, persistor), Throws.TypeOf<SymphonyControllerException>());

        }

        [Test]
        public void ShouldDiscardEpochsWithExceptionalStop()
        {
            var c = new Controller();
            bool evt = false;

            c.DAQController = new ExceptionThrowingDAQController();
            c.DAQController.Clock = c.DAQController as IClock;
            c.Clock = c.DAQController as IClock;
            var persistor = new FakeEpochPersistor();

            c.DiscardedEpoch += (co, args) => evt = true;

            var e = new Epoch(UNUSED_PROTOCOL);

            try
            {
                c.RunEpoch(e, persistor);
            }
            catch (Exception)
            { }

            Assert.True(evt);
            Assert.False(persistor.PersistedEpochs.ToList().Contains(e));
        }

        [Test, Timeout(2000)]
        public void ShouldSupplyEpochBackgroundForExternalDevicesWithoutStimuli()
        {
            Converters.Register("V", "V",
                (IMeasurement m) => m);

            var daq = new SimpleDAQController();
            var c = new NonValidatingController { DAQController = daq };

            var dev1 = new UnitConvertingExternalDevice("dev1", "co", c, new Measurement(0, "V"));
            var dev2 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"));

            dev1.BindStream(new DAQOutputStream("out1"));
            dev2.BindStream(new DAQOutputStream("out2"));

            int baseSamples = 1000;
            IList<IMeasurement> data = (IList<IMeasurement>)Enumerable.Range(0, baseSamples)
                .Select(i => new Measurement(i, "V") as IMeasurement)
                .ToList();

            Measurement sampleRate = new Measurement(baseSamples, "Hz");

            c.BackgroundDataStreams[dev1] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, sampleRate));
            c.BackgroundDataStreams[dev2] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, sampleRate));

            var config = new Dictionary<string, object>();

            IOutputData data1 = new OutputData(data, sampleRate, true);

            var e = new Epoch("");
            e.Stimuli[dev1] = new RenderedStimulus((string) "RenderedStimulus", (IDictionary<string, object>) config, data1);

            var backgroundMeasurement = new Measurement(3.2m, "V");
            e.Backgrounds[dev2] = new Background(backgroundMeasurement, sampleRate);

            IOutputData out1 = null;
            IOutputData out2 = null;
            bool pulled = false;
            daq.Started += (evt, args) =>
            {
                out1 = c.PullOutputData(dev1, e.Duration);
                out2 = c.PullOutputData(dev2, e.Duration);
                pulled = true;

                c.RequestStop();
            };

            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!pulled)
            {
                Thread.Sleep(1);
            }

            Assert.NotNull(out1);

            Assert.NotNull(out2);
            Assert.AreEqual((TimeSpan)e.Duration, out2.Duration);
            Assert.AreEqual(backgroundMeasurement, out2.Data.First());
        }

        private const string UNUSED_PROTOCOL = "unused-protocol";

        [Test]
        public void RunEpochThrowsGivenEpochWithInconsistentStimulusDurations()
        {
            Converters.Register("V", "V",
                // just an identity conversion for now, to pass Validate()
                    (IMeasurement m) => m);

            var c = new Controller { DAQController = new SimpleDAQController2() };
            c.DAQController.Clock = c.DAQController as IClock;

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev1 = new UnitConvertingExternalDevice("dev1", "co", c, new Measurement(0, "V"))
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock
            };

            var dev2 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"))
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock
            };

            var sampleRate = new Measurement(1, "Hz");

            var back = new Background(UNUSED_BACKGROUND, sampleRate);
            c.BackgroundDataStreams[dev1] = new BackgroundOutputDataStream(back);
            c.BackgroundDataStreams[dev2] = new BackgroundOutputDataStream(back);

            e.Stimuli[dev1] = new RenderedStimulus((string) "ID1",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) new OutputData(new List<IMeasurement> { new Measurement(1, "V") },
                                                                                sampleRate, true));

            e.Stimuli[dev2] = new RenderedStimulus((string) "ID2",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) new OutputData(new List<IMeasurement> { new Measurement(1, "V"), new Measurement(1, "V") },
                                                                                sampleRate, true));

            Assert.That(() => c.RunEpoch(e, new FakeEpochPersistor()), Throws.Exception.TypeOf<ArgumentException>());

            e.Stimuli[dev2] = new DelegatedStimulus("ID2", "V", sampleRate,
                                                    new Dictionary<string, object>(),
                                                    (x, y) => null,
                                                    (p) => Option<TimeSpan>.None());

            Assert.That(() => c.RunEpoch(e, new FakeEpochPersistor()), Throws.Exception.TypeOf<ArgumentException>());

        }

        [Test]
        public void RunEpochThrowsGivenIndefiniteEpochWithResponses()
        {
            Converters.Register("V", "V",
                // just an identity conversion for now, to pass Validate()
                    (IMeasurement m) => m);

            var c = new Controller { DAQController = new SimpleDAQController2() };
            c.DAQController.Clock = c.DAQController as IClock;

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev2 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"))
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock
            };

            var sampleRate = new Measurement(1, "Hz");

            var back = new Background(UNUSED_BACKGROUND, sampleRate);
            c.BackgroundDataStreams[dev2] = new BackgroundOutputDataStream(back);

            e.Stimuli[dev2] = new DelegatedStimulus("ID2", "units", new Measurement(10, "Hz"), 
                                                    new Dictionary<string, object>(),
                                                    (x, y) => null,
                                                    (p) => Option<TimeSpan>.None());
            e.Responses[dev2] = new Response();

            Assert.That(() => c.RunEpoch(e, new FakeEpochPersistor()), Throws.Exception.TypeOf<ArgumentException>());
        }

        [Test]
        [Timeout(5000)]
        public void RunEpochThrowsWhenRunningSimultaneousEpochs()
        {
            Converters.Register("V", "V",
                // just an identity conversion for now, to pass Validate()
                                (IMeasurement m) => m);

            var c = new Controller { DAQController = new SimpleDAQController() };
            c.DAQController.Clock = c.DAQController as IClock;

            var sampleRate = new Measurement(1, "Hz");

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev = new UnitConvertingExternalDevice("dev", "co", c, new Measurement(0, "V"))
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock,
                OutputSampleRate = sampleRate
            };
            var outStream = new DAQOutputStream("out")
            {
                MeasurementConversionTarget = "V",
                Clock = c.Clock
            };
            dev.BindStream(outStream);

            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, sampleRate));

            e.Stimuli[dev] = new DelegatedStimulus("ID1", "units", sampleRate, new Dictionary<string, object>(),
                                                    (parameters, duration) => null,
                                                    objects => Option<TimeSpan>.None());

            bool started = false;
            c.Started += (evt, args) =>
            {
                started = true;
            };

            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!started)
            {
                Thread.Sleep(1);
            }

            Assert.That(() => c.RunEpoch(e, new FakeEpochPersistor()), Throws.Exception.TypeOf<SymphonyControllerException>());
        }

        [Test]
        public void EpochValidates()
        {
            var c = new Controller();

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev2 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"));

            var sampleRate = new Measurement(1, "Hz");

            e.Stimuli[dev2] = new DelegatedStimulus("ID2", "units", new Measurement(1000, "Hz"), 
                                                    new Dictionary<string, object>(),
                                                    (x, y) => null,
                                                    (p) => Option<TimeSpan>.None());


            try
            {
                Assert.That(() => c.RunEpoch(e, new FakeEpochPersistor()), Throws.Nothing);
            }
            catch (Exception)
            {
                //pass
            }

        }

        private readonly IExternalDevice devFake = new UnitConvertingExternalDevice("DevFake", "DevManufacturer",
                                                                                    new Measurement(0, "V"));

        private readonly IDAQStream streamFake = new DAQOutputStream("StreamFake");

        [Test]
        [Timeout(5000)]
        public void ShouldTruncateResponseAtEpochBoundary()
        {
            Converters.Register("V", "V",
                (IMeasurement m) => m);

            var daq = new SimpleDAQController();
            var c = new NonValidatingController { DAQController = daq };
            var dev = new UnitConvertingExternalDevice("dev", UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND);
            var outStream = new DAQOutputStream("out");
            var inStream = new DAQInputStream("in");
            dev.BindStream(outStream).BindStream(inStream);

            var sampleRate = new Measurement(1, "Hz");

            c.BackgroundDataStreams[dev] = new BackgroundOutputDataStream(new Background(UNUSED_BACKGROUND, sampleRate));

            var samples = new List<IMeasurement> { new Measurement(1, "V"), new Measurement(2, "V"), new Measurement(3, "V") };

            var data = new OutputData(samples,
                                      sampleRate, true);

            var e = new Epoch(UNUSED_PROTOCOL);

            e.Stimuli[dev] = new RenderedStimulus((string) "ID1",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) data);
            e.Responses[dev] = new Response();

            bool pushed = false;
            daq.Started += (evt, args) =>
            {
                c.PullOutputData(dev, data.Duration);
                c.PushInputData(dev, new InputData(samples.Concat(samples).ToList(),
                                                   sampleRate,
                                                   DateTimeOffset.Now)
                                         .DataWithStreamConfiguration(streamFake, new Dictionary<string, object>())
                                         .DataWithExternalDeviceConfiguration(devFake, new Dictionary<string, object>()));
                pushed = true;

                c.RequestStop();
            };

            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!pushed)
            {
                Thread.Sleep(1);
            }

            Assert.That(((TimeSpan)e.Responses[dev].Duration), Is.EqualTo((TimeSpan)e.Duration));
        }

        [Test]
        [Ignore("Allowing NextEpoch is sort of a mess. We may re-implement this later.")]
        public void NexEpochShouldDequeueEpoch()
        {
            var c = new Controller();

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev1 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"));

            var sampleRate = new Measurement(1, "Hz");

            var samples = new List<IMeasurement> { new Measurement(1, "V"), new Measurement(2, "V"), new Measurement(3, "V") };

            var data = new OutputData(samples,
                                      sampleRate, true);

            e.Stimuli[dev1] = new RenderedStimulus((string) "ID1",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) data);
            e.Responses[dev1] = new Response();

            c.EnqueueEpoch(e);

            //Assert.That(c.CurrentEpoch, Is.Null);

            //c.NextEpoch();

            //Assert.That(c.CurrentEpoch, Is.EqualTo(e));
        }

        [Test]
        [Ignore("Allowing NextEpoch is sort of a mess. We may re-implement this later.")]
        public void NextEpochThrowsIfCannotDequeue()
        {
            var c = new Controller();

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev1 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"));

            var sampleRate = new Measurement(1, "Hz");

            var samples = new List<IMeasurement> { new Measurement(1, "V"), new Measurement(2, "V"), new Measurement(3, "V") };

            var data = new OutputData(samples,
                                      sampleRate, true);

            e.Stimuli[dev1] = new RenderedStimulus((string) "ID1",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) data);
            e.Responses[dev1] = new Response();

            c.EnqueueEpoch(e);

            //c.NextEpoch();

            //Assert.Throws<SymphonyControllerException>(() => c.NextEpoch());
        }

        [Test]
        public void GetsStreamsByName()
        {
            string name1 = "IN1";
            string name2 = "IN2";
            string name3 = "OUT1";

            IDAQStream in1 = new DAQInputStream(name1);
            IDAQStream in2 = new DAQInputStream(name2);
            IDAQStream in3 = new DAQInputStream(name1);
            IDAQStream out1 = new DAQOutputStream(name3);
            var c = new SimpleDAQController();
            (c as IMutableDAQController).AddStream(in1);
            (c as IMutableDAQController).AddStream(in2);
            (c as IMutableDAQController).AddStream(in3);
            (c as IMutableDAQController).AddStream(out1);

            Assert.AreEqual(2, c.GetStreams(name1).Count());
            Assert.AreEqual(1, c.GetStreams(name2).Count());
            Assert.AreEqual(1, c.GetStreams(name3).Count());

            Assert.AreEqual(in2, c.GetStreams(name2).First());
            Assert.AreEqual(out1, c.GetStreams(name3).First());


        }

        [Test, Timeout(2000)]
        public void PushesDataToEpoch()
        {
            const string UNUSED_NAME = "UNUSED";

            var daq = new SimpleDAQController();
            var c = new NonValidatingController { DAQController = daq };
            var dev = new UnitConvertingExternalDevice(UNUSED_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND);
            var outStream = new DAQOutputStream("out");
            var inStream = new DAQInputStream("in");

            dev.BindStream(outStream).BindStream(inStream);

            var srate = new Measurement(100, "Hz");
            var samples = Enumerable.Range(0, 100).Select(i => new Measurement(1, "V")).ToList();

            var e = new Epoch("PushesDataToEpoch");
            e.Responses[dev] = new Response();

            e.Stimuli[dev] = new RenderedStimulus((string) "ID1", (IDictionary<string, object>) new Dictionary<string, object>(), (IOutputData) new OutputData(samples, srate, false));

            var data = new InputData(samples, srate, DateTimeOffset.Now).DataWithStreamConfiguration(inStream, new Dictionary<string, object>());
            bool pushed = false;

            daq.Started += (evt, args) =>
                {
                    c.PushInputData(dev, data);
                    pushed = true;

                    c.RequestStop();
                };

            c.EnqueueEpoch(e);
            c.StartAsync(null);

            while (!pushed)
            {
                Thread.Sleep(1);
            }

            Assert.That(e.Responses[dev].Data, Is.EqualTo(data.Data));
            Assert.That(e.Responses[dev].InputTime, Is.EqualTo(data.InputTime));
            Assert.That(e.Responses[dev].DataConfigurationSpans.First().Nodes.First(),
                Is.EqualTo(data.NodeConfigurationWithName(inStream.Name)));
        }

        [Test, Timeout(5000)]
        public void ShouldNotPersistEpochGivenNullPersistor()
        {
            var c = new Controller();
            bool evt = false;

            c.DAQController = new TestDAQController();
            c.Clock = c.DAQController as IClock;

            Converters.Register("V", "V",
                // just an identity conversion for now, to pass Validate()
                    (IMeasurement m) => m);

            c.DiscardedEpoch += (co, args) => Assert.Fail("Run failed");

            var srate = new Measurement(10, "Hz");

            var dev = new UnitConvertingExternalDevice(UNUSED_DEVICE_NAME, UNUSED_DEVICE_MANUFACTURER, c, UNUSED_BACKGROUND)
            {
                Clock = c.Clock,
                MeasurementConversionTarget = "V",
                OutputSampleRate = srate,
                InputSampleRate = srate
            };

            var back = new Background(UNUSED_BACKGROUND, srate);
            c.BackgroundDataStreams.Add(dev, new BackgroundOutputDataStream(back));

            var outStream = new DAQOutputStream("outStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            var inStream = new DAQInputStream("inStream") { MeasurementConversionTarget = "V", Clock = c.Clock };

            (c.DAQController as IMutableDAQController).AddStream(outStream);
            (c.DAQController as IMutableDAQController).AddStream(inStream);

            outStream.SampleRate = srate;
            inStream.SampleRate = srate;

            dev.BindStream(outStream);
            dev.BindStream(inStream);

            var e = new Epoch(UNUSED_PROTOCOL);
            var samples = new List<IMeasurement> { new Measurement(1.0m, "V"), new Measurement(1.0m, "V"), new Measurement(1.0m, "V") };
            var data = new OutputData(samples,
                srate,
                true);

            e.Stimuli[dev] = new RenderedStimulus((string) "stimID",
                                                  (IDictionary<string, object>) new Dictionary<string, object>(),
                                                  (IOutputData) data);
            e.Responses[dev] = new Response();

            ((TestDAQController)c.DAQController).AddStreamMapping(outStream, inStream);

            c.RunEpoch(e, null);
            
            Assert.Pass();
        }

        [Test]
        [Ignore("Allowing NextEpoch is sort of a mess. We may re-implement this later.")]
        public void NextEpochShouldFireNextEpochEvent()
        {
            var c = new Controller();
            c.Clock = new FakeClock();

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev1 = new UnitConvertingExternalDevice("dev2", "co", c, new Measurement(0, "V"));

            var sampleRate = new Measurement(1, "Hz");

            var samples = new List<IMeasurement> { new Measurement(1, "V"), new Measurement(2, "V"), new Measurement(3, "V") };

            var data = new OutputData(samples,
                                      sampleRate, true);

            e.Stimuli[dev1] = new RenderedStimulus((string) "ID1",
                                                   (IDictionary<string, object>) new Dictionary<string, object>(),
                                                   (IOutputData) data);
            e.Responses[dev1] = new Response();

            c.EnqueueEpoch(e);

            bool evt = false;
            //c.NextEpochRequested += (sender, args) => evt = true;

            //c.NextEpoch();

            Assert.True(evt);
        }

        [Test]
        [Timeout(5 * 1000)]
        public void RunEpochShouldDiscardEpochWhenRequestStopCalled()
        {
            Converters.Register("V", "V",
                // just an identity conversion for now, to pass Validate()
                                (IMeasurement m) => m);

            var c = new Controller { DAQController = new SimpleDAQController2() };
            c.DAQController.Clock = c.DAQController as IClock;

            var sampleRate = new Measurement(1, "Hz");

            var e = new Epoch(UNUSED_PROTOCOL);
            var dev1 = new UnitConvertingExternalDevice("dev1", "co", c, new Measurement(0, "V"))
                {
                    MeasurementConversionTarget = "V",
                    Clock = c.Clock,
                    OutputSampleRate = sampleRate
                };
            var outStream = new DAQOutputStream("out")
                {
                    MeasurementConversionTarget = "V",
                    Clock = c.Clock
                };
            dev1.BindStream(outStream);

            var back = new Background(UNUSED_BACKGROUND, sampleRate);
            c.BackgroundDataStreams[dev1] = new BackgroundOutputDataStream(back);

            e.Stimuli[dev1] = new DelegatedStimulus("ID1", "units", sampleRate, new Dictionary<string, object>(),
                                                    (parameters, duration) =>
                                                    new OutputData(new List<IMeasurement>(), sampleRate, false),
                                                    objects => Option<TimeSpan>.None());
            
            bool epochDiscarded = false;
            c.DiscardedEpoch += (sender, args) =>
                                    {
                                        epochDiscarded = true;
                                    };


            c.DAQController.ProcessIteration += (o, eventArgs) =>
                                       {
                                           Console.WriteLine("Process iteration");
                                           c.RequestStop();
                                       };

            c.RunEpoch(e, new FakeEpochPersistor());

            Assert.True(epochDiscarded);
        }

        [Test]
        public void RunEpochShouldRespectEpochWaitForTrigger()
        {
            var c = new Controller();
            
            var daq = new SimpleDAQController();
            daq.Clock = new SystemClock();

            c.DAQController = daq;

            var e = new Epoch(UNUSED_PROTOCOL) { WaitForTrigger = true };

            new TaskFactory().StartNew(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                c.RequestStop();
            },
            TaskCreationOptions.LongRunning);

            c.RunEpoch(e, new FakeEpochPersistor());

            Assert.IsTrue(daq.WaitedForTrigger);
        }

        class NonValidatingController : Controller
        {
            protected override Maybe<string> ValidateEpoch(Epoch epoch)
            {
                return Maybe<string>.Yes();
            }

            public override Maybe<string> Validate()
            {
                return Maybe<string>.Yes();
            }
        }
    }

    class SimpleDAQController : DAQControllerBase, IClock, IMutableDAQController
    {
        public SimpleDAQController() : this(new IDAQStream[] {})
        {
        }
        public SimpleDAQController(IDAQStream[] streams)
        {
            foreach (IDAQStream i in streams)
            {
                DAQStreams.Add(i);
            }

            BackgroundSet = false;
            WaitedForTrigger = false;
            ProcessInterval = TimeSpan.FromSeconds(0.25);
        }

        public void SetRunning(bool runnig)
        {
            IsRunning = runnig;
        }

        protected override void StartHardware(bool wait)
        {
            WaitedForTrigger = wait;
        }

        public IMeasurement AsyncBackground { get; set; }

        public override void ApplyStreamBackgroundAsync(IDAQOutputStream s, IMeasurement background)
        {
            AsyncBackground = background;
        }

        protected override IDictionary<IDAQInputStream, IInputData> ProcessLoopIteration(IDictionary<IDAQOutputStream, IOutputData> outData, TimeSpan deficit, CancellationToken token)
        {
            return new Dictionary<IDAQInputStream, IInputData>();
        }

        public override void SetStreamsBackground()
        {
            BackgroundSet = true;
        }

        public bool BackgroundSet { get; set; }
        public bool WaitedForTrigger { get; set; }

        public DateTimeOffset Now { get { return DateTimeOffset.Now; } }
        public void AddStream(IDAQStream stream)
        {
            DAQStreams.Add(stream);
        }

        protected override bool ShouldStop()
        {
            return IsStopRequested;
        }
    }

    class SimpleDAQController2 : SimpleDAQController
    {
        protected override bool ShouldStop()
        {
            return IsStopRequested;
        }
    }

    class ExceptionThrowingDAQController : SimpleDAQController, IClock
    {

        protected override IDictionary<IDAQInputStream, IInputData> ProcessLoopIteration(IDictionary<IDAQOutputStream, IOutputData> outData, TimeSpan deficit, CancellationToken token)
        {
            throw new Exception("Exception thrown in ProcessLoopIteration");
        }

        protected override bool ShouldStop()
        {
            return IsStopRequested;
        }
    }

}
