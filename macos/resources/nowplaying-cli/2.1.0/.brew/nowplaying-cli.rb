class NowplayingCli < Formula
  desc "Retrieves currently playing media, and simulates media actions"
  homepage "https://github.com/kirtan-shah/nowplaying-cli"
  url "https://github.com/kirtan-shah/nowplaying-cli/archive/refs/tags/v2.1.0.tar.gz"
  sha256 "c7c23564657bf5b5598bdf58f79e11c8dffbde909e364df4da7c3a49a26e3753"
  license "GPL-3.0-or-later"

  depends_on :macos

  def install
    system "make", "install", "PREFIX=#{prefix}"
  end

  test do
    assert_match "{", shell_output("#{bin}/nowplaying-cli get-raw")
  end
end
