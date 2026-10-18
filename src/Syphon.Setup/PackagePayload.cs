using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Syphon.Setup;
internal static class PackagePayload
{
 private static readonly byte[] Magic=Encoding.ASCII.GetBytes("SYPHON-PAYLOAD01");
#if SETUP_DIAGNOSTICS
 internal static string? TestSource;
#endif
 internal static Stream Open() {
#if SETUP_DIAGNOSTICS
  var source=TestSource ?? Environment.ProcessPath!;
#else
  var source=Environment.ProcessPath!;
#endif
  var file=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  try {
   long end=file.Length;
   // Authenticode appends its certificate after the signed payload. Its PE directory
   // identifies that boundary, so signing does not change payload discovery.
   using(var reader=new BinaryReader(file,Encoding.UTF8,true)) {
    file.Position=0x3c;var pe=reader.ReadInt32();
    if(pe>0&&pe<file.Length-256) {file.Position=pe+24;var kind=reader.ReadUInt16();var directory=pe+24+(kind==0x20b?112:96)+32;file.Position=directory;var cert=reader.ReadUInt32();var size=reader.ReadUInt32();if(cert>0&&size>0&&cert+(long)size==file.Length)end=cert;}
   }
   for(int pad=0;pad<8;pad++) {
    if(end<64+pad)break;file.Position=end-64-pad;var footer=new byte[64];file.ReadExactly(footer);
    if(!footer.AsSpan(0,16).SequenceEqual(Magic))continue;
    var start=BitConverter.ToInt64(footer,16);var length=BitConverter.ToInt64(footer,24);
    if(start<=0||length<=0||start+length!=end-64-pad)throw new InvalidDataException("Invalid installer payload boundary.");
    var slice=new SliceStream(file,start,length);
    var digest=SHA256.HashData(slice);if(!digest.AsSpan().SequenceEqual(footer.AsSpan(32,32)))throw new InvalidDataException("The installer payload is damaged. Download it again.");
    slice.Position=0;return slice;
   }
   throw new IOException("This copy is an uninstaller. Open the full Syphon Setup to install.");
  }catch{file.Dispose();throw;}
 }
 private sealed class SliceStream(FileStream file,long start,long length):Stream {
  private long position;
  public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>true;public override long Length=>length;
  public override long Position{get=>position;set{if(value<0||value>length)throw new IOException("Invalid package position.");position=value;}}
  public override int Read(byte[] buffer,int offset,int count){file.Position=start+position;var read=file.Read(buffer,offset,(int)Math.Min(count,length-position));position+=read;return read;}
  public override int Read(Span<byte> buffer){file.Position=start+position;var read=file.Read(buffer[..(int)Math.Min(buffer.Length,length-position)]);position+=read;return read;}
  public override long Seek(long offset,SeekOrigin origin){Position=origin switch{SeekOrigin.Begin=>offset,SeekOrigin.Current=>position+offset,_=>length+offset};return position;}
  public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();protected override void Dispose(bool disposing){if(disposing)file.Dispose();base.Dispose(disposing);}
 }
}
