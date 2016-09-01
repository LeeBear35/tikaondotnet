using System;
using System.Linq;
using java.io;
using javax.xml.transform;
using javax.xml.transform.sax;
using javax.xml.transform.stream;
using org.apache.tika.io;
using org.apache.tika.metadata;
using org.apache.tika.parser;
using Exception = System.Exception;

namespace TikaOnDotNet.TextExtraction
{
	public interface ITextExtractor
	{
		/// <summary>
		/// Extract text from a given filepath.
		/// </summary>
		/// <param name="filePath">File path to be extracted.</param>
		TextExtractionResult Extract(string filePath);
		
		/// <summary>
		/// Extract text from a byte[]. This is a good way to get data from arbitrary sources.
		/// </summary>
		/// <param name="data">A byte array of data which will have its text extracted.</param>
		TextExtractionResult Extract(byte[] data);

    		/// <summary>
    		/// Extract text from a byte[]. This is a good way to get data from arbitrary sources.
    		/// </summary>
    		/// <param name="data">A byte array of data which will have its text extracted.</param>
    		/// <param name="filePath">A string containing the file name to help the detector determine the proper parser</param>
    		/// <param name="ContentType">A string that has the mime type to help the detector determine the correct parser to use</param>
    		TextExtractionResult Extract(byte[] data, string filePath, string ContentType);
    		
    		/// <summary>
		/// Extract text from a URI. Time to create your very of web spider.
		/// </summary>
		/// <param name="uri">URL which will have its text extracted.</param>
		TextExtractionResult Extract(Uri uri);

		/// <summary>
		/// Under the hood we are using Tika which is a Java project. Tika wants an java.io.InputStream. The other overloads eventually call this Extract giving this method a Func.
		/// </summary>
		/// <param name="streamFactory">A Func which takes a Metadata object and returns an InputStream.</param>
		/// <returns></returns>
		TextExtractionResult Extract(Func<Metadata, InputStream> streamFactory);
	}

	public class TextExtractor : ITextExtractor
	{
    		private static TikaConfig config = TikaConfig.getDefaultConfig();
    		private TesseractOCRConfig tesseractOCRConfig;
    		private static string tesseractPath = string.Empty;
    		public string TesseractPath 
    		{ 
      			get { return tesseractPath; } 
      			set 
      			{ 
        			tesseractPath = value;
        			tesseractOCRConfig = new TesseractOCRConfig();
        			//todo: validate directory and tesseract.exe at location
        			tesseractOCRConfig.setTesseractPath(tesseractPath);
      			} 
    		}
    		
    		public bool IsOCRPathEnabled 
    		{ 
      			get { return tesseractOCRConfig != null; } 
      			set
      			{
        			if (value)
        			{
          				tesseractOCRConfig = new TesseractOCRConfig();
          				tesseractOCRConfig.setTesseractPath(tesseractPath);
        			}
        			else
        			{
          				tesseractOCRConfig = null;
        			}
      			}
    		}

		public TextExtractionResult Extract(string filePath)
		{
			try
			{
				var inputStream = new FileInputStream(filePath);
				return Extract(metadata =>
				{
					var result = TikaInputStream.get(inputStream);
					metadata.add("FilePath", filePath);
					return result;
				});
			}
			catch (Exception ex)
			{
				throw new TextExtractionException("Extraction of text from the file '{0}' failed.".ToFormat(filePath), ex);
			}
		}

		public TextExtractionResult Extract(byte[] data)
		{
      			return Extract(data, string.Empty, string.Empty);
		}

	    	public TextExtractionResult Extract(byte[] data, string filePath, string ContentType)
    		{
      			TextExtractionResult result = Extract
        		(
          			metadata =>
          			{
            				metadata.add(org.apache.tika.metadata.TikaMetadataKeys.__Fields.RESOURCE_NAME_KEY, System.IO.Path.GetFileName(filePath));
            				metadata.add(org.apache.tika.metadata.TikaMimeKeys.__Fields.TIKA_MIME_FILE, filePath);
            				try
            				{
              					if (!ContentType.Equals(org.apache.tika.mime.MimeTypes.OCTET_STREAM, StringComparison.CurrentCultureIgnoreCase))
              					{
                					metadata.add(org.apache.tika.metadata.HttpHeaders.__Fields.CONTENT_TYPE, ContentType);
              					}
              					else
              					{
                					Detector detector = config.getDetector();
                					using (org.apache.tika.io.TikaInputStream inputStream = org.apache.tika.io.TikaInputStream.@get(data, metadata))
                					{
                  						MediaType foundType = detector.detect(inputStream, metadata);
                  						if (!foundType.toString().Equals(org.apache.tika.mime.MimeTypes.OCTET_STREAM, StringComparison.CurrentCultureIgnoreCase))
                  						{
                    							metadata.add(org.apache.tika.metadata.HttpHeaders.__Fields.CONTENT_TYPE, foundType.toString());
                  						}
                					}
              					}
            				}
            				catch (Exception ex)
            				{
              					throw ex;
            				}

            				return TikaInputStream.get(data, metadata);
          			}
        		);

      			return result;
    		}

		public TextExtractionResult Extract(Uri uri)
		{
			var jUri = new java.net.URI(uri.ToString());
			return Extract(metadata =>
			{
				var result = TikaInputStream.get(jUri, metadata);
				metadata.add("Uri", uri.ToString());
				return result;
			});
		}

		public TextExtractionResult Extract(Func<Metadata, InputStream> streamFactory)
		{
			try
			{
				var parser = new AutoDetectParser();
				var metadata = new Metadata();
				var outputWriter = new StringWriter();
				var parseContext = new ParseContext();

        			if (IsOCRPathEnabled)
        			{
          				parseContext.set(typeof(TesseractOCRConfig), tesseractOCRConfig);
        			}

                //use the base class type for the key or parts of Tika won't find a usable parser
				parseContext.set(typeof(Parser), parser);
				
				using (var inputStream = streamFactory(metadata))
				{
					try
					{
						parser.parse(inputStream, getTransformerHandler(outputWriter), metadata, parseContext);
					}
					finally
					{
						inputStream.close();
					}
				}

				return AssembleExtractionResult(outputWriter.ToString(), metadata);
			}
			catch (Exception ex)
			{
				throw new TextExtractionException("Extraction failed.", ex);
			}
		}

		private static TextExtractionResult AssembleExtractionResult(string text, Metadata metadata)
		{
			var metaDataResult = metadata.names()
				.ToDictionary(name => name, name => string.Join(", ", metadata.getValues(name)));

			var contentType = metaDataResult["Content-Type"];

			return new TextExtractionResult
			{
				Text = text,
				ContentType = contentType,
				Metadata = metaDataResult
			};
		}

		private static TransformerHandler getTransformerHandler(Writer output)
		{
			var factory = (SAXTransformerFactory) TransformerFactory.newInstance();
			var transformerHandler = factory.newTransformerHandler();
			
			transformerHandler.getTransformer().setOutputProperty(OutputKeys.METHOD, "text");
			transformerHandler.getTransformer().setOutputProperty(OutputKeys.INDENT, "yes");

			transformerHandler.setResult(new StreamResult(output));
			return transformerHandler;
		}
	}
}
