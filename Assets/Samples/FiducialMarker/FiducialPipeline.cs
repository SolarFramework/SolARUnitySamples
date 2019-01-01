/**
* @copyright Copyright (c) 2017 B-com http://www.b-com.com/
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*     http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using SolAR.Api.Features;
using SolAR.Api.Geom;
using SolAR.Api.Image;
using SolAR.Api.Input.Files;
using SolAR.Api.Solver.Pose;
using SolAR.Core;
using SolAR.Datastructure;
using UnityEngine.Assertions;
using XPCF;

namespace SolAR.Samples
{
    public class FiducialPipeline : AbstractPipeline
    {
        // structures
        readonly Image greyImage;
        readonly Image binaryImage;

        readonly Contour2DfList contours;
        readonly Contour2DfList filtered_contours;
        readonly ImageList patches;
        readonly Contour2DfList recognizedContours;
        readonly DescriptorBuffer recognizedPatternsDescriptors;
        readonly DescriptorBuffer markerPatternDescriptor;
        readonly DescriptorMatchVector patternMatches;
        readonly Point2DfList pattern2DPoints;
        readonly Point2DfList img2DPoints;
        readonly Point3DfList pattern3DPoints;
        readonly Transform3Df pose;

        // components
        readonly IMarker2DSquaredBinary binaryMarker;

        readonly IImageConvertor imageConvertor;
        readonly IImageFilter imageFilterBinary;
        readonly IContoursExtractor contoursExtractor;
        readonly IContoursFilter contoursFilter;
        readonly IPerspectiveController perspectiveController;
        readonly IDescriptorsExtractorSBPattern patternDescriptorExtractor;

        readonly IDescriptorMatcher patternMatcher;
        readonly ISBPatternReIndexer patternReIndexer;

        readonly IImage2WorldMapper img2worldMapper;
        readonly I3DTransformFinderFrom2D3D PnP;

        public FiducialPipeline(IComponentManager xpcfComponentManager) : base(xpcfComponentManager)
        {
            // structures
            greyImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);
            binaryImage = SharedPtr.Alloc<Image>().AddTo(subscriptions);

            contours = new Contour2DfList().AddTo(subscriptions);
            filtered_contours = new Contour2DfList().AddTo(subscriptions);
            patches = new ImageList().AddTo(subscriptions);
            recognizedContours = new Contour2DfList().AddTo(subscriptions);
            recognizedPatternsDescriptors = new DescriptorBuffer().AddTo(subscriptions);
            markerPatternDescriptor = new DescriptorBuffer().AddTo(subscriptions);
            patternMatches = new DescriptorMatchVector().AddTo(subscriptions);
            pattern2DPoints = new Point2DfList().AddTo(subscriptions);
            img2DPoints = new Point2DfList().AddTo(subscriptions);
            pattern3DPoints = new Point3DfList().AddTo(subscriptions);
            pose = SharedPtr.Alloc<Transform3Df>();

            // components
            binaryMarker = xpcfComponentManager.create("SolARMarker2DSquaredBinaryOpencv").bindTo<IMarker2DSquaredBinary>().AddTo(subscriptions);

            imageConvertor = xpcfComponentManager.create("SolARImageConvertorOpencv").bindTo<IImageConvertor>().AddTo(subscriptions);
            imageFilterBinary = xpcfComponentManager.create("SolARImageFilterBinaryOpencv").bindTo<IImageFilter>().AddTo(subscriptions);
            contoursExtractor = xpcfComponentManager.create("SolARContoursExtractorOpencv").bindTo<IContoursExtractor>().AddTo(subscriptions);
            contoursFilter = xpcfComponentManager.create("SolARContoursFilterBinaryMarkerOpencv").bindTo<IContoursFilter>().AddTo(subscriptions);
            perspectiveController = xpcfComponentManager.create("SolARPerspectiveControllerOpencv").bindTo<IPerspectiveController>().AddTo(subscriptions);
            patternDescriptorExtractor = xpcfComponentManager.create("SolARDescriptorsExtractorSBPatternOpencv").bindTo<IDescriptorsExtractorSBPattern>().AddTo(subscriptions);

            patternMatcher = xpcfComponentManager.create("SolARDescriptorMatcherRadiusOpencv").bindTo<IDescriptorMatcher>().AddTo(subscriptions);
            patternReIndexer = xpcfComponentManager.create("SolARSBPatternReIndexer").bindTo<ISBPatternReIndexer>().AddTo(subscriptions);

            img2worldMapper = xpcfComponentManager.create("SolARImage2WorldMapper4Marker2D").bindTo<IImage2WorldMapper>().AddTo(subscriptions);
            PnP = xpcfComponentManager.create("SolARPoseEstimationPnpOpencv").bindTo<I3DTransformFinderFrom2D3D>().AddTo(subscriptions);

            // components initialisation
            ok = binaryMarker.loadMarker();
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);
            ok = patternDescriptorExtractor.extract(binaryMarker.getPattern(), markerPatternDescriptor);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Set the size of the box to display according to the marker size in world unit

            int patternSize = binaryMarker.getPattern().getSize();

            patternDescriptorExtractor.bindTo<IConfigurable>().getProperty("patternSize").setIntegerValue(patternSize);
            patternReIndexer.bindTo<IConfigurable>().getProperty("sbPatternSize").setIntegerValue(patternSize);

            // NOT WORKING ! initialize image mapper with the reference image size and marker size
            var img2worldMapperConf = img2worldMapper.bindTo<IConfigurable>();
            img2worldMapperConf.getProperty("digitalWidth").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("digitalHeight").setIntegerValue(patternSize);
            img2worldMapperConf.getProperty("worldWidth").setFloatingValue(binaryMarker.getSize().width);
            img2worldMapperConf.getProperty("worldHeight").setFloatingValue(binaryMarker.getSize().height);
        }

        public Sizef GetMarkerSize(){ return binaryMarker.getSize(); }
        public void SetCameraParameters(Matrix3x3 intrinsics, CamDistortion distorsion) {PnP.setCameraParameters(intrinsics, distorsion); }

        protected FrameworkReturnCode Proceed(Image inputImage)
        {
            // Convert Image from RGB to grey
            ok = imageConvertor.convert(inputImage, greyImage, Image.ImageLayout.LAYOUT_GREY);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Convert Image from grey to black and white
            ok = imageFilterBinary.filter(greyImage, binaryImage);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Extract contours from binary image
            ok = contoursExtractor.extract(binaryImage, contours);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Filter 4 edges contours to find those candidate for marker contours
            ok = contoursFilter.filter(contours, filtered_contours);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // Create one warpped and cropped image by contour
            ok = perspectiveController.correct(binaryImage, filtered_contours, patches);
            Assert.AreEqual(FrameworkReturnCode._SUCCESS, ok);

            // test if this last image is really a squared binary marker, and if it is the case, extract its descriptor
            if (patternDescriptorExtractor.extract(patches, filtered_contours, recognizedPatternsDescriptors, recognizedContours) == FrameworkReturnCode._SUCCESS)
            {
                // From extracted squared binary pattern, match the one corresponding to the squared binary marker
                if (patternMatcher.match(markerPatternDescriptor, recognizedPatternsDescriptors, patternMatches) == DescriptorMatcherRetCode.DESCRIPTORS_MATCHER_OK)
                {
                    // Reindex the pattern to create two vector of points, the first one corresponding to marker corner, the second one corresponding to the poitsn of the contour
                    patternReIndexer.reindex(recognizedContours, patternMatches, pattern2DPoints, img2DPoints);

                    // Compute the 3D position of each corner of the marker
                    img2worldMapper.map(pattern2DPoints, pattern3DPoints);

                    // Compute the pose of the camera using a Perspective n Points algorithm using only the 4 corners of the marker
                    if (PnP.estimate(img2DPoints, pattern3DPoints, pose) == FrameworkReturnCode._SUCCESS)
                    {
                        return FrameworkReturnCode._SUCCESS;
                    }
                }
            }
            return FrameworkReturnCode._ERROR_;
        }
    }
}
