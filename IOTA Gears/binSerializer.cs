//using IOTAGears.EntityModels;
//using MessagePack;
//using MessagePack.Formatters;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace IOTAGears
//{
//    public class TransactionFormatter<TransactionContainer> : IMessagePackFormatter<TransactionContainer>
//    {
//        public TransactionContainer Deserialize(byte[] bytes, int offset, IFormatterResolver formatterResolver, out int readSize)
//        {
//            throw new NotImplementedException();
//        }

//        public int Serialize(ref byte[] bytes, int offset, TransactionContainer value, IFormatterResolver formatterResolver)
//        {
//            if (value == null)
//            {
//                return MessagePackBinary.WriteNil(ref bytes, offset);
//            }
//            throw new NotImplementedException();
//        }

        
//    }
    
//    public static class BinSerializer
//    {
//        public static string Serialize(object obj)
//        {
//            MessagePack.Resolvers.CompositeResolver.RegisterAndSetAsDefault(
//                new[] { new TransactionFormatter<TransactionContainer>() },
//                new[] { MessagePack.Resolvers.ContractlessStandardResolver.Instance, MessagePack.Resolvers.StandardResolver.Instance }
//                );

//            var bin = MessagePackSerializer.Serialize(obj, MessagePack.Resolvers.CompositeResolver.Instance);
//            return MessagePackSerializer.ToJson(bin);
//            //return bin;
//        }
//    }
//}
