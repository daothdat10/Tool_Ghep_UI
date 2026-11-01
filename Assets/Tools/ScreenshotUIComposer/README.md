# Screenshot UI Composer — Offline + 9Router

Mở Unity: **Tools > UI Composer > From Screenshot**.

1. Kéo ảnh popup PNG/JPG vào tool.
2. Bấm **Dựng popup**.
3. Bấm **Mở prefab vừa dựng**. Kết quả tự lưu trong `Assets/GeneratedUI/Popup_<thời gian>/`.

Với project Bus, nguồn icon/font đã mặc định `Assets/Sprite` và `Assets/Font`. Project khác chỉ cần chọn và ghi nhớ nguồn một lần trong mục **Nguồn icon / font**. Hỗ trợ đường dẫn tuyệt đối bên trong project; thư mục ngoài project có nút import sao chép sang Assets. Các font TTF/OTF được chuyển sang TMP tự động nếu chưa có TMP font.

Chế độ mặc định chạy offline trên **Windows Unity Editor**, không cần API key hay Python. Có thể bật 9Router theo hướng dẫn dưới đây. OCR dùng thành phần Windows.Media.Ocr có sẵn trên máy; cần ít nhất một ngôn ngữ OCR của Windows. Đã kiểm tra OCR hoạt động trên máy này. Ảnh tối đa 4096px mỗi cạnh.

## Tool tự làm

- Tìm vùng popup sáng nổi bật, loại nền game phía sau, thêm lớp dim trong suốt.
- Phân vùng header, nút mua, nút đóng, minh họa và icon vàng khi nhận diện được.
- So hình với sprite nguồn; chỉ dùng nguồn khi điểm khớp đủ tốt. Trường hợp không chắc, tạo sprite bổ sung từ ảnh mẫu.
- Tạo PNG có alpha, xóa chữ khỏi đồ họa bằng nội suy màu nền cục bộ và tạo chữ TMP riêng.
- OCR hai lượt để đọc cả chữ thông thường và số có viền; so hình glyph render của các font để chọn font gần ảnh nhất.
- Nút được nhận diện có component Button để gắn sự kiện sau này. Tool không suy ra logic mua hàng hay hành động đóng popup.
- Lưu prefab, layout JSON, ảnh bổ sung, material viền chữ, kết quả OCR và build-report.json cạnh nhau.

**Tạo asset bổ sung ở đây là tách/khôi phục hình từ ảnh mẫu**, không phải sinh tranh mới. Phần đồ họa bị che được ước lượng theo màu xung quanh; không thể đảm bảo tái tạo chi tiết chưa xuất hiện trong ảnh. Chữ nhỏ hoặc chữ trang trí nằm trong minh họa có thể vẫn nằm trong sprite. Các sprite nguồn phải được import đúng kiểu Sprite.

Bản hiện tại dùng quy tắc màu/hình học phù hợp popup game sáng trên nền tối. Không đảm bảo mọi kiểu ảnh, popup tối, nền phức tạp hoặc nhiều cửa sổ chồng nhau đều nhận diện đúng. Có mục **Chỉnh thêm (tùy chọn)** để sửa layer/text rồi lưu bản khác. Ảnh overlay có thể bật lại bằng thanh chồng ảnh để đối chiếu.

## Kiểm tra

Đã chạy trực tiếp với ảnh popup +1 Bus do người dùng cung cấp trên Unity 2022.3.23f1. OCR đọc được tiêu đề, hai dòng mô tả và giá 600; nút đóng/nút mua có Button; có prefab thật và ảnh render kiểm tra tại `TestResults/UIComposer/offline-preview.png`. Đây là kiểm tra một ảnh cụ thể, không phải bảo đảm khớp tuyệt đối với mọi ảnh.

Các lần thử được lưu thành thư mục riêng trong Assets/GeneratedUI. Kết quả cuối cùng được ghi trong `TestResults/UIComposer/offline-result.json`. Thư mục output của từng lần dựng có báo cáo số sprite nguồn dùng lại, số sprite bổ sung và số lớp chữ.

## Cài vào project khác

Import `ScreenshotUIComposer.unitypackage`. Cần UGUI, TextMeshPro và Newtonsoft.Json. Gói cài gồm code Editor và script OCR Windows; không kèm icon/font riêng của Bus. File OCR nằm cạnh code Editor, không đổi đường dẫn thư mục tool sau khi import.

Kết quả cuối ảnh mẫu: 11 layer, tái sử dụng Sprite_Currency_Coin_Single_01 từ nguồn, tạo 5 sprite bổ sung, 4 lớp TMP và 2 Button. Các kiểm tra mở lại prefab và tham chiếu nguồn đã đạt; Console không có lỗi biên dịch.

## Kết nối API 9Router (tùy chọn)

Trong tool, bật **Dùng 9Router để hoàn thiện text / UI**:

1. **Base URL**: địa chỉ 9Router, ví dụ `http://localhost:20128/v1`. Tool nhận cả URL gốc và URL kết thúc bằng `/v1/chat/completions`.
2. **API key**: nhập key của 9Router nếu đã bật xác thực. Có thể để trống khi router không yêu cầu key.
3. Bấm **Kiểm tra kết nối / Tải model**.
4. **Model nhận diện**: chọn model có khả năng đọc ảnh. Danh sách chat có thể chứa cả model chỉ đọc chữ, nên chọn đúng model vision.
5. **Model tạo ảnh**: chọn model hỗ trợ `/v1/images/generations` trả JSON/base64/URL hoặc PNG/JPEG đồng bộ. Có thể nhập ID model trực tiếp nếu danh sách tự tải không có.
6. **Tối đa ảnh mới**: mặc định 3; 0 để chỉ sửa bố cục/text và dùng sprite hiện có. Model yêu cầu quá giới hạn sẽ dừng trước khi gửi yêu cầu tạo ảnh.
7. Chỉ bật **Model hỗ trợ alpha** nếu model ảnh hỗ trợ tham số background=transparent.
8. Bấm **Ghi nhớ URL / Model**, sau đó kéo ảnh và bấm **Dựng popup · 9Router**.

Tool giữ bản dựng offline trước, rồi yêu cầu model sửa text/bố cục, dùng sprite nguồn và tạo sprite thiếu theo mô tả. Chữ vẫn là TextMeshPro; panel đơn giản có thể là UI Image màu đặc. Bản AI lưu trong thư mục `9Router` cạnh bản offline. Nếu API lỗi/hủy, prefab offline vẫn được giữ. Sprite do AI tạo cần kiểm tra hình dạng, nền alpha và độ giống ảnh; model ảnh nhận mô tả chi tiết từ model phân tích, không tự bảo đảm giống tuyệt đối.

URL/model được lưu trong EditorPrefs theo project. API key chỉ giữ trong bộ nhớ, không được ghi vào project hay EditorPrefs. Có thể cấp `NINEROUTER_URL` và `NINEROUTER_KEY` trước khi mở Unity. Khi bật 9Router, ảnh mẫu, bố cục và danh mục asset được gửi tới endpoint cấu hình; các yêu cầu tạo ảnh có thể phát sinh phí theo provider. Key của router không được gửi kèm khi tải ảnh từ URL lưu trữ do provider trả về.

Hiện chưa hỗ trợ phản hồi ảnh dạng SSE hoặc job bất đồng bộ phải polling. Nếu provider/model cần định dạng riêng, hãy chọn model tương thích API đồng bộ ở trên. Chưa kiểm tra với 9Router thật của người dùng vì chưa có địa chỉ/model cụ thể.

Kiểm tra giao thức bằng mock server cục bộ đã đạt: tải model, gửi ảnh vision, bố cục JSON, tạo 2 sprite qua base64 và URL, TMP còn chỉnh sửa được, mở lại prefab, hủy request, che key trong lỗi và chuẩn hóa URL. Kết quả tại `TestResults/UIComposer/router-status.txt`. Mock server đã tắt sau kiểm tra.

Tài liệu giao thức: [9Router Chat](https://github.com/decolua/9router/blob/master/skills/9router-chat/SKILL.md), [9Router Image](https://github.com/decolua/9router/blob/master/skills/9router-image/SKILL.md).

### Khi gặp HTTP 503
Nếu chi tiết kèm [403] hoặc HTTP 403, router đã nhận được request nhưng provider từ chối quyền truy cập. Model list HTTP 200 không xác nhận inference hoạt động. Tool kiểm tra loại model trước khi dựng; model chat không được dùng làm model tạo ảnh khi server đã công bố danh sách model ảnh. Chọn model từ danh sách ảnh hoặc đặt giới hạn ảnh mới bằng 0 nếu chỉ sửa text/bố cục.

## Bản đối chiếu Shuffle
Đã bổ sung ShuffleReferenceBuilder cho đúng ảnh Shuffle đang kiểm tra. Mở Chỉnh thêm > Khớp ảnh Shuffle mẫu để dựng lại. Đây là bộ dựng theo bố cục riêng của ảnh mẫu, không phải bộ nhận diện chung cho mọi popup. Prefab: Assets/GeneratedUI/ShuffleMatched_20261102_003741/Shuffle.prefab. Tiêu đề, subtitle, khung có chữ mô tả và CTA giữ pixel từ ảnh nguồn để không mất chữ uốn, bóng và viền. Nhóm EditableTextAlternatives có 4 TMP, tắt mặc định: khi thay nội dung cần tắt artwork tương ứng và bật TMP. Chiếc mũ dùng Sprite_Booster_Shuffle_Large_Colored; quầng sáng mềm là texture thủ tục gần đúng. Prefab không chứa nền gameplay, có overlay dim để đặt trên scene thật. Render đối chiếu tại TestResults/UIComposer/shuffle-matched.png. Model API cũng được nhắc không đoán sprite bằng tên, không chọn tia cứng thay halo mềm, không dùng nền caro và phải giữ line breaks.
